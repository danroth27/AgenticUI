// Keeps streaming content pinned to its newest output: chat transcripts, and the
// artifact panels that scenarios like the agentic plan write into progressively.
// The prototype chat components style a scroll-to-bottom affordance but never
// scroll anything themselves, so long output streams in below the fold.
// Following stops as soon as the reader scrolls up, and resumes when they
// return to the bottom.

const SELECTORS = ['.sc-ai-chat-page__body', '.scenario__panel'];
const THRESHOLD_PX = 40;
const tracked = new WeakSet();

function isAtBottom(el) {
    return el.scrollHeight - el.clientHeight - el.scrollTop <= THRESHOLD_PX;
}

function track(el) {
    if (tracked.has(el)) {
        return;
    }
    tracked.add(el);

    let stick = true;
    el.addEventListener('scroll', () => stick = isAtBottom(el), { passive: true });

    new MutationObserver(() => {
        if (stick) {
            el.scrollTop = el.scrollHeight;
        }
    }).observe(el, { childList: true, subtree: true, characterData: true });

    el.scrollTop = el.scrollHeight;
}

function trackAll() {
    SELECTORS.forEach(s => document.querySelectorAll(s).forEach(track));
}

// Transcripts are created and replaced as Blazor renders and as the user navigates,
// so watch the document rather than binding once at startup.
new MutationObserver(trackAll).observe(document.body, { childList: true, subtree: true });
trackAll();
