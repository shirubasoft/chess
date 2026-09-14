namespace Chess;

public union CastlingRights(NoCastlingRights, KingSideCastlingRights, QueenSideCastlingRights, BothCastlingRights);

public sealed record NoCastlingRights;

public sealed record KingSideCastlingRights;

public sealed record QueenSideCastlingRights;

public sealed record BothCastlingRights;
