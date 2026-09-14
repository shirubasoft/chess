#!/usr/bin/env python3
"""Compare the actual .NET executable with independent chess implementations."""

import argparse
import contextlib
import hashlib
import io
import json
import pathlib
import queue
import random
import re
import subprocess
import sys
import threading
import traceback

import chess
import chess.pgn


ROOT = pathlib.Path(__file__).resolve().parents[2]
HERE = pathlib.Path(__file__).resolve().parent


class Process:
    def __init__(self, command):
        self.process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                        stderr=sys.stderr, text=True, bufsize=1)
        self.lines = queue.Queue()
        threading.Thread(target=self._read, daemon=True).start()

    def _read(self):
        for line in self.process.stdout:
            self.lines.put(line.rstrip("\n"))
        self.lines.put(None)

    def send(self, text):
        self.process.stdin.write(text + "\n")
        self.process.stdin.flush()

    def line(self):
        try:
            result = self.lines.get(timeout=60)
        except queue.Empty as error:
            raise RuntimeError("Engine response timed out") from error
        if result is None:
            raise RuntimeError(f"Engine terminated with code {self.process.poll()}")
        return result

    def request(self, **request):
        self.send(json.dumps(request))
        response = json.loads(self.line())
        if isinstance(response, dict) and "error" in response:
            raise AssertionError(f"Request {request!r} failed: {response['error']!r}")
        return response

    def close(self):
        if self.process.poll() is None:
            self.process.stdin.close()
            try:
                self.process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait()
        self.process.stdout.close()


class Stockfish(Process):
    def __init__(self, path):
        super().__init__([path])
        self.send("uci")
        self.version = None
        while (line := self.line()) != "uciok":
            if line.startswith("id name "):
                self.version = line[8:]
        self.send("setoption name Threads value 1")
        self.send("isready")
        while self.line() != "readyok":
            pass

    def perft(self, fen, depth):
        self.send(f"position fen {fen}")
        self.send(f"go perft {depth}")
        moves = {}
        while True:
            line = self.line()
            if line.startswith("Nodes searched:"):
                return {"nodes": int(line.split(":")[1]), "moves": moves}
            match = re.fullmatch(r"([a-h][1-8][a-h][1-8][qrbn]?): (\d+)", line)
            if match:
                moves[match[1]] = int(match[2])


def equal(actual, expected, label):
    if actual != expected:
        raise AssertionError(f"{label}: expected {expected!r}, got {actual!r}")


def outcome(board):
    if board.is_checkmate():
        return "checkmate"
    if board.is_stalemate():
        return "stalemate"
    if board.is_insufficient_material():
        return "dead_position"
    return "ongoing"


def position_check(engine, board, expected_outcome=None):
    fen = board.fen(en_passant="fen")
    response = engine.request(operation="position", fen=fen)
    equal(response["fen"], fen, "FEN")
    equal(response["roundtripFen"], fen, "JSON position round trip")
    equal(response["roundtripKeyMatches"], True, "JSON repetition identity")
    equal(response["inCheck"], board.is_check(), "check")
    equal(response["outcome"], expected_outcome or outcome(board), "position outcome")
    expected = {}
    for move in list(board.legal_moves):
        san = board.san(move)
        board.push(move)
        expected[move.uci()] = {"fen": board.fen(en_passant="fen"), "san": san}
        board.pop()
    equal(response["moves"], expected, "complete legal move set, SAN, and successor FENs")
    return response


def history_check(engine, initial, moves):
    board = chess.Board(initial)
    keys = [board.fen().split()[:4]]
    for text in moves:
        board.push_uci(text)
        keys.append(board.fen().split()[:4])
    response = engine.request(operation="history", fen=initial, moves=moves)
    fen = board.fen(en_passant="fen")
    equal(response["fen"], fen, "history FEN")
    equal(response["occurrences"], keys.count(keys[-1]), "repetition count")
    equal(response["replayFen"], fen, "serialized event replay")
    equal(response["replayResult"], response["result"], "replayed match result")
    equal(response["revision"], len(moves), "event revision")
    equal(response["replayRevision"], len(moves), "replayed revision")
    result = board.outcome()
    reasons = {chess.Termination.CHECKMATE: "Checkmate", chess.Termination.STALEMATE: "Stalemate",
               chess.Termination.INSUFFICIENT_MATERIAL: "DeadPosition",
               chess.Termination.SEVENTYFIVE_MOVES: "SeventyFiveMoveRule",
               chess.Termination.FIVEFOLD_REPETITION: "FivefoldRepetition"}
    expected_result = None if result is None else {
        "winner": None if result.winner is None else "white" if result.winner else "black",
        "reason": reasons[result.termination]}
    equal(response["result"], expected_result, "automatic match outcome")
    for key, current_test in [("threefold", lambda: board.is_repetition(3)), ("fiftyMove", board.is_fifty_moves)]:
        current = result is None and current_test()
        intended = []
        if result is None:
            for move in list(board.legal_moves):
                board.push(move)
                if current_test():
                    intended.append(move.uci())
                board.pop()
        equal(response[key], {"current": current, "moves": sorted(intended)}, key + " claims")


def pgn_check(engine, text):
    actual = engine.request(operation="pgn", text=text)
    stream = io.StringIO(text)
    expected = []
    while (game := chess.pgn.read_game(stream)) is not None:
        if game.errors:
            raise AssertionError(game.errors)
        board = game.board()
        moves = []
        for move in game.mainline_moves():
            san = board.san(move)
            board.push(move)
            moves.append((move.uci(), san, board.fen(en_passant="fen")))
        tree = {}

        def visit(node, path):
            board = node.board()
            for child in node.variations:
                key = path + (child.move.uci(),)
                tree[key] = (board.san(child.move), child.board().fen(en_passant="fen"))
                visit(child, key)

        visit(game, ())
        expected.append((game.board().fen(en_passant="fen"), moves, tree))
    equal(len(actual), len(expected), "PGN game count")
    for parsed, (fen, moves, expected_tree) in zip(actual, expected):
        equal(parsed["initialFen"], fen, "PGN initial position")
        equal([(move["uci"], move["san"], move["fen"]) for move in parsed["line"]["moves"]], moves, "PGN mainline")
        actual_tree = {}

        def visit_line(line, path):
            for move in line["moves"]:
                for variation in move["variations"]:
                    visit_line(variation, path)
                path += (move["uci"],)
                actual_tree[path] = (move["san"], move["fen"])

        visit_line(parsed["line"], ())
        equal(actual_tree, expected_tree, "PGN variation positions")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--engine", default=str(ROOT / "tools/Chess.Validation/bin/Release/net11.0/Chess.Validation"))
    parser.add_argument("--stockfish", required=True)
    parser.add_argument("--seed", type=int, default=20260914)
    parser.add_argument("--games", type=int, default=12)
    parser.add_argument("--plies", type=int, default=80)
    parser.add_argument("--corpus", type=pathlib.Path, required=True)
    parser.add_argument("--report", type=pathlib.Path, default=ROOT / "artifacts/validation-report.json")
    args = parser.parse_args()
    if args.games < 1 or args.plies < 1:
        parser.error("games and plies must be positive")
    report = {"seed": args.seed, "pythonChess": chess.__version__, "checks": [], "failures": [],
              "limitations": ["General FIDE dead-position reachability remains outside the core detector's documented scope."]}

    def check(name, context, action):
        try:
            action()
            report["checks"].append({"name": name, "context": context})
        except Exception as error:
            report["failures"].append({"name": name, "context": context, "error": str(error), "traceback": traceback.format_exc()})
            print(f"FAIL {name}: {context}: {error}", flush=True)

    try:
        corpus_bytes = args.corpus.read_bytes()
        if not corpus_bytes.strip():
            raise ValueError("The perft corpus is empty")
        report["corpusSha256"] = hashlib.sha256(corpus_bytes).hexdigest()
        with contextlib.closing(Process([args.engine])) as engine, contextlib.closing(Stockfish(args.stockfish)) as reference:
            report["stockfish"] = reference.version
            for number, line in enumerate(corpus_bytes.decode().splitlines(), 1):
                if not line.strip():
                    continue
                fields = line.split(";")
                fen = fields[0].strip()
                expected = {int(depth): int(nodes) for depth, nodes in re.findall(r"D(\d+)\s+(\d+)", line)}
                for depth in (1, 2):
                    def compare(fen=fen, depth=depth, expected=expected):
                        actual = engine.request(operation="perft", fen=fen, depth=depth)
                        stockfish = reference.perft(fen, depth)
                        equal(actual, stockfish, "Stockfish per-move counts")
                        if depth in expected:
                            equal(actual["nodes"], expected[depth], "published perft total")
                    check("perft-corpus", {"line": number, "fen": fen, "depth": depth, "publishedTotal": expected.get(depth)}, compare)
                if number <= 2:
                    check("perft-depth-3", {"fen": fen}, lambda fen=fen: equal(
                        engine.request(operation="perft", fen=fen, depth=3), reference.perft(fen, 3), "Stockfish depth 3"))
            print("Perft corpus complete", flush=True)

            for scenario in json.loads((HERE / "fide-scenarios.json").read_text()):
                def scenario_check(scenario=scenario):
                    board = chess.Board(scenario["fen"])
                    snapshot = position_check(engine, board, scenario.get("outcome"))
                    for text in scenario.get("legal", []):
                        equal(text in snapshot["moves"], True, "FIDE legal move " + text)
                    for text in scenario.get("illegal", []):
                        equal(text in snapshot["moves"], False, "FIDE illegal move " + text)
                        rejection = engine.request(operation="move", fen=scenario["fen"], notation="uci", move=text)
                        equal(rejection["accepted"], False, "illegal request rejected")
                        equal(rejection["fen"], scenario["fen"], "rejection preserves state")
                check("FIDE", scenario, scenario_check)

            knight_cycle = ["g1f3", "g8f6", "f3g1", "f6g8"]
            for length in (0, 7, 8, 15, 16):
                moves = (knight_cycle * 4)[:length]
                check("FIDE-9.2-9.6-repetition", {"moves": moves}, lambda moves=moves: history_check(engine, chess.STARTING_FEN, moves))
            for clock in (98, 99, 100, 149, 150):
                initial = chess.STARTING_FEN.replace(" 0 1", f" {clock} 1")
                check("FIDE-9.3-9.6-clock", {"fen": initial}, lambda initial=initial: history_check(engine, initial, []))
            for move in ("g1f3", "e2e4"):
                initial = chess.STARTING_FEN.replace(" 0 1", " 149 1")
                check("FIDE-9.6-clock-move", {"fen": initial, "move": move}, lambda move=move: history_check(engine, initial, [move]))
            mating = "7k/8/5KQ1/8/8/8/8/8 w - - 149 1"
            check("FIDE-9.6.2-mate-precedence", {"fen": mating}, lambda: history_check(engine, mating, ["g6g7"]))

            randomizer = random.Random(args.seed)
            for game_number in range(args.games):
                board = chess.Board()
                moves = []
                game = chess.pgn.Game()
                node = game
                for ply in range(args.plies):
                    check("random-position", {"game": game_number, "ply": ply, "fen": board.fen(en_passant="fen")}, lambda: position_check(engine, board))
                    if board.is_game_over():
                        break
                    move = randomizer.choice(sorted(board.legal_moves, key=lambda item: item.uci()))
                    san = board.san(move)
                    expected_uci = move.uci()
                    check("SAN-import", {"fen": board.fen(en_passant="fen"), "san": san}, lambda: equal(
                        engine.request(operation="move", fen=board.fen(en_passant="fen"), notation="san", move=san)["uci"], expected_uci, "SAN resolved move"))
                    board.push(move)
                    moves.append(move.uci())
                    node = node.add_variation(move)
                check("random-history", {"game": game_number, "moves": moves}, lambda: history_check(engine, chess.STARTING_FEN, moves))
                pgn = str(game)
                check("PGN-import", {"game": game_number, "pgn": pgn}, lambda: pgn_check(engine, pgn))
                print(f"Game {game_number + 1}/{args.games} complete", flush=True)
            check("PGN-external-sample", {}, lambda: pgn_check(engine, (HERE / "sample.pgn").read_text()))
            annotated = '[Result "*"]\n\n1. e4! {King pawn} (1. d4 d5 (1... Nf6)) e5 2. Nf3 $1 Nc6 *'
            check("PGN-annotated-variations", {"pgn": annotated}, lambda: pgn_check(engine, annotated))
            with contextlib.closing(Process([args.engine, "--uci"])) as protocol:
                protocol.send("uci")
                while protocol.line() != "uciok":
                    pass
                protocol.send("isready")
                equal(protocol.line(), "readyok", "UCI ready handshake")
                protocol.send("position startpos moves e2e4 e7e5")
                protocol.send("go perft 1")
                actual = {}
                while not (line := protocol.line()).startswith("Nodes searched:"):
                    move, count = line.split(":")
                    actual[move] = int(count)
                board = chess.Board()
                board.push_uci("e2e4")
                board.push_uci("e7e5")
                check("UCI-perft-adapter", {}, lambda: equal(actual, {move.uci(): 1 for move in board.legal_moves}, "UCI move sequence and divide"))
                protocol.send("quit")
    except Exception as error:
        report["failures"].append({"name": "infrastructure", "error": str(error), "traceback": traceback.format_exc()})
    finally:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, indent=2) + "\n")
    print(f"{len(report['checks'])} checks passed; {len(report['failures'])} failed. Report: {args.report}", flush=True)
    return bool(report["failures"])


if __name__ == "__main__":
    sys.exit(main())
