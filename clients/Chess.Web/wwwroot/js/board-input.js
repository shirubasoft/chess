// Keep pointer tracking local; Blazor and the server validate the completed move.
export function attach(board, receiver) {
    const events = new AbortController();
    const options = { signal: events.signal };
    let gesture = null;
    let suppressClick = false;

    const squareAt = (x, y) => {
        const square = document.elementFromPoint(x, y)?.closest('[data-square]');
        return square && board.contains(square) ? square : null;
    };
    const current = () => gesture?.context === board.dataset.dragContext;

    function finish(to = null, notify = true) {
        const previous = gesture;
        gesture = null;
        if (!previous) return;
        previous.ghost?.remove();
        board.classList.remove('dragging');
        board.querySelectorAll('.drag-source, .drag-target, .drag-over').forEach(square =>
            square.classList.remove('drag-source', 'drag-target', 'drag-over'));
        if (board.hasPointerCapture(previous.pointerId)) board.releasePointerCapture(previous.pointerId);
        if (previous.started && notify) {
            // Finish touchend before rendering the promotion chooser; inserting it
            // during pointerup can make Chromium suppress the next tap's click.
            setTimeout(() => {
                if (!events.signal.aborted)
                    receiver.invokeMethodAsync('DropBoardPiece', previous.source.dataset.square, to, previous.context);
            }, 0);
        }
    }

    board.addEventListener('pointerdown', event => {
        if (gesture) { finish(); return; }
        suppressClick = false;
        if (!event.isPrimary || event.button !== 0) return;
        const source = event.target.closest('[data-square]');
        if (!source || source.disabled || !source.dataset.destinations) return;
        gesture = { source, pointerId: event.pointerId, x: event.clientX, y: event.clientY,
            context: board.dataset.dragContext, started: false };
    }, options);

    window.addEventListener('pointermove', event => {
        if (!gesture || event.pointerId !== gesture.pointerId) return;
        if (!current() || event.buttons === 0) { finish(); return; }
        if (!gesture.started) {
            if (Math.hypot(event.clientX - gesture.x, event.clientY - gesture.y) < 6) return;
            gesture.started = true;
            suppressClick = true;
            board.setPointerCapture(event.pointerId);
            const bounds = gesture.source.getBoundingClientRect();
            const ghost = document.createElement('div');
            ghost.className = 'drag-piece';
            ghost.setAttribute('aria-hidden', 'true');
            ghost.style.width = `${bounds.width}px`;
            ghost.style.height = `${bounds.height}px`;
            ghost.append(gesture.source.querySelector('.piece').cloneNode(true));
            document.body.append(ghost);
            gesture.ghost = ghost;
            board.classList.add('dragging');
            gesture.source.classList.add('drag-source');
            for (const target of gesture.source.dataset.destinations.split(' '))
                board.querySelector(`[data-square="${target}"]`)?.classList.add('drag-target');
        }
        event.preventDefault();
        gesture.ghost.style.left = `${event.clientX}px`;
        gesture.ghost.style.top = `${event.clientY}px`;
        board.querySelector('.drag-over')?.classList.remove('drag-over');
        const target = squareAt(event.clientX, event.clientY);
        if (target?.classList.contains('drag-target')) target.classList.add('drag-over');
    }, { ...options, passive: false });

    window.addEventListener('pointerup', event => {
        if (!gesture || event.pointerId !== gesture.pointerId) return;
        const target = current() ? squareAt(event.clientX, event.clientY)?.dataset.square : null;
        finish(target);
    }, options);
    window.addEventListener('pointercancel', event => {
        if (event.pointerId === gesture?.pointerId) finish();
    }, options);
    board.addEventListener('lostpointercapture', event => {
        // Touch begins with implicit capture on the square, which is handed to the board.
        if (event.target === board && event.pointerId === gesture?.pointerId) finish();
    }, options);
    board.addEventListener('click', event => {
        // A completed/cancelled drag must not become a second, synthetic board click.
        if (suppressClick && event.detail !== 0) {
            event.preventDefault();
            event.stopImmediatePropagation();
        }
    }, { ...options, capture: true });
    board.addEventListener('dragstart', event => event.preventDefault(), options);
    window.addEventListener('keydown', event => {
        if (event.key === 'Escape' && gesture) { event.preventDefault(); finish(); }
    }, options);
    window.addEventListener('blur', () => finish(), options);
    window.addEventListener('resize', () => finish(), options);
    window.addEventListener('scroll', () => finish(), { ...options, capture: true });
    const observer = new MutationObserver(() => { if (gesture && !current()) finish(); });
    observer.observe(board, { attributes: true, attributeFilter: ['data-drag-context'] });
    board.dataset.dragReady = 'true';
    return { dispose() { finish(null, false); observer.disconnect(); events.abort(); delete board.dataset.dragReady; } };
}
