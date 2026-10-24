import React, { useCallback, useEffect, useRef, useState } from 'react';
import Icon from 'Components/Icon';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { useScrollButtonsOptions } from './scrollButtonsStore';
import styles from './ScrollButtons.css';

interface ScrollState {
  canScroll: boolean;
  atTop: boolean;
  atBottom: boolean;
}

const MIN_SCROLLABLE_DISTANCE = 150;
const EDGE_THRESHOLD = 40;

// The element that scrolls is the innermost page scroller with overflowing content
function getScroller() {
  const scrollers = document.querySelectorAll<HTMLElement>(
    '[data-page-scroller]'
  );
  const scroller = Array.from(scrollers)
    .reverse()
    .find((e) => e.scrollHeight - e.clientHeight > MIN_SCROLLABLE_DISTANCE);

  // On small screens the whole document scrolls
  const documentScroller = document.scrollingElement as HTMLElement | null;

  return (
    scroller ??
    (documentScroller &&
    documentScroller.scrollHeight - documentScroller.clientHeight >
      MIN_SCROLLABLE_DISTANCE
      ? documentScroller
      : null)
  );
}

function ScrollButtons() {
  const { showScrollButtons, splitScrollButtons } = useScrollButtonsOptions();
  const scrollerRef = useRef<HTMLElement | null>(null);
  const [state, setState] = useState<ScrollState>({
    canScroll: false,
    atTop: true,
    atBottom: false,
  });

  const update = useCallback(() => {
    const el = getScroller();

    scrollerRef.current = el;

    const next = el
      ? {
          canScroll: true,
          atTop: el.scrollTop < EDGE_THRESHOLD,
          atBottom:
            el.scrollHeight - el.clientHeight - el.scrollTop < EDGE_THRESHOLD,
        }
      : { canScroll: false, atTop: true, atBottom: false };

    setState((prev) =>
      prev.canScroll === next.canScroll &&
      prev.atTop === next.atTop &&
      prev.atBottom === next.atBottom
        ? prev
        : next
    );
  }, []);

  useEffect(() => {
    if (!showScrollButtons) {
      return;
    }

    update();

    // scroll events do not bubble, but they can be captured
    document.addEventListener('scroll', update, true);

    // content and routes change without scrolling
    const interval = window.setInterval(update, 500);

    return () => {
      document.removeEventListener('scroll', update, true);
      window.clearInterval(interval);
    };
  }, [showScrollButtons, update]);

  const handleTopPress = useCallback(() => {
    scrollerRef.current?.scrollTo({ top: 0, behavior: 'smooth' });
  }, []);

  const handleBottomPress = useCallback(() => {
    const el = scrollerRef.current;

    el?.scrollTo({ top: el.scrollHeight, behavior: 'smooth' });
  }, []);

  if (!showScrollButtons || !state.canScroll) {
    return null;
  }

  // Single button: down while at the top, up everywhere else.
  // Split buttons: both always shown, the one that cannot be used is disabled.
  const showTop = splitScrollButtons || !state.atTop;
  const showBottom = splitScrollButtons || state.atTop;

  return (
    <div className={styles.container}>
      {showTop ? (
        <button
          className={styles.button}
          title={translate('ScrollToTop')}
          disabled={state.atTop}
          onClick={handleTopPress}
        >
          <Icon name={icons.SCROLL_UP} />
        </button>
      ) : null}

      {showBottom ? (
        <button
          className={styles.button}
          title={translate('ScrollToBottom')}
          disabled={state.atBottom}
          onClick={handleBottomPress}
        >
          <Icon name={icons.SCROLL_DOWN} />
        </button>
      ) : null}
    </div>
  );
}

export default ScrollButtons;
