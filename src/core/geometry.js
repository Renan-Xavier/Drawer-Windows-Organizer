(function (root, factory) {
  if (typeof module === 'object' && module.exports) {
    module.exports = factory();
  } else {
    root.DrawerGeometry = factory();
  }
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  const SCALE_CONFIGS = {
    small: {
      scale: 'small',
      drawerSize: 48,
      iconSize: 32,
      cellWidth: 56,
      cellHeight: 62,
      borderRadius: 12,
      previewIconSize: 16,
      padding: 8,
      gap: 6,
    },
    medium: {
      scale: 'medium',
      drawerSize: 64,
      iconSize: 48,
      cellWidth: 74,
      cellHeight: 82,
      borderRadius: 16,
      previewIconSize: 22,
      padding: 10,
      gap: 8,
    },
    large: {
      scale: 'large',
      drawerSize: 88,
      iconSize: 64,
      cellWidth: 96,
      cellHeight: 104,
      borderRadius: 22,
      previewIconSize: 30,
      padding: 12,
      gap: 10,
    },
  };

  function getScaleMetrics(scale = 'medium') {
    return SCALE_CONFIGS[scale] ? { ...SCALE_CONFIGS[scale] } : { ...SCALE_CONFIGS.medium };
  }

  /**
   * Computes exact rectangular grid (cols, rows, width, height) for a drawer's open container.
   * Inspired by Discord server folders: keeps a clean geometric rounded rectangle/square
   * that adjusts tightly to the exact number of icons inside.
   */
  function computeDrawerGrid(iconCount = 0, direction = 'right', scale = 'medium') {
    const metrics = getScaleMetrics(scale);
    const count = Math.max(1, Number(iconCount) || 0);
    const maxMainAxis = 4;

    let cols;
    let rows;

    if (direction === 'down') {
      rows = Math.min(count, maxMainAxis);
      cols = Math.ceil(count / maxMainAxis);
    } else {
      cols = Math.min(count, maxMainAxis);
      rows = Math.ceil(count / maxMainAxis);
    }

    const width =
      metrics.padding * 2 +
      cols * metrics.cellWidth +
      Math.max(0, cols - 1) * metrics.gap;

    const height =
      metrics.padding * 2 +
      rows * metrics.cellHeight +
      Math.max(0, rows - 1) * metrics.gap;

    return {
      cols,
      rows,
      width,
      height,
      metrics,
    };
  }

  /**
   * Determines which direction ('right' | 'down' | 'left') the drawer should slide open
   * based on its position on the screen and available space.
   */
  function computeOpenDirection({
    x = 0,
    y = 0,
    iconCount = 1,
    scale = 'medium',
    screenWidth = 1920,
    screenHeight = 1080,
    preferredDirection = 'auto',
    margin = 16,
  } = {}) {
    const metrics = getScaleMetrics(scale);
    const drawerSize = metrics.drawerSize;
    const connectorGap = 8;

    const fitsDirection = (dir) => {
      const grid = computeDrawerGrid(iconCount, dir, scale);
      if (dir === 'right') {
        return x + drawerSize + connectorGap + grid.width + margin <= screenWidth;
      }
      if (dir === 'left') {
        return x - connectorGap - grid.width >= margin;
      }
      if (dir === 'down') {
        return y + drawerSize + connectorGap + grid.height + margin <= screenHeight;
      }
      return false;
    };

    if (
      preferredDirection &&
      preferredDirection !== 'auto' &&
      ['right', 'down', 'left'].includes(preferredDirection)
    ) {
      if (fitsDirection(preferredDirection)) {
        return preferredDirection;
      }
    }

    // Priority order when automatic: right -> left (if near right side) / down -> fallback
    const candidates =
      x > screenWidth * 0.65
        ? ['right', 'left', 'down']
        : ['right', 'down', 'left'];

    for (const dir of candidates) {
      if (fitsDirection(dir)) {
        return dir;
      }
    }

    // Fallback to direction with most available room
    const roomRight = screenWidth - (x + drawerSize);
    const roomLeft = x;
    const roomDown = screenHeight - (y + drawerSize);

    if (roomRight >= roomLeft && roomRight >= roomDown) return 'right';
    if (roomLeft >= roomRight && roomLeft >= roomDown) return 'left';
    return 'down';
  }

  /**
   * Manages hover (with 1.25s persistence after mouseLeave) and click-to-pin state transitions.
   */
  function createInteractionMachine({ hoverDelayMs = 1250, onChange } = {}) {
    let state = 'closed'; // 'closed' | 'hover-open' | 'hover-closing' | 'pinned'
    let closeTimer = null;

    const clearTimer = () => {
      if (closeTimer !== null) {
        clearTimeout(closeTimer);
        closeTimer = null;
      }
    };

    const getState = () => ({
      isOpen: state !== 'closed',
      isPinned: state === 'pinned',
      state,
    });

    const notify = () => {
      if (typeof onChange === 'function') {
        onChange(getState());
      }
    };

    const mouseEnter = () => {
      clearTimer();
      if (state === 'pinned') {
        return getState();
      }
      if (state !== 'hover-open') {
        state = 'hover-open';
        notify();
      }
      return getState();
    };

    const mouseLeave = () => {
      clearTimer();
      if (state === 'pinned' || state === 'closed') {
        return getState();
      }
      state = 'hover-closing';
      notify();
      closeTimer = setTimeout(() => {
        closeTimer = null;
        if (state === 'hover-closing') {
          state = 'closed';
          notify();
        }
      }, hoverDelayMs);
      return getState();
    };

    const toggleClick = () => {
      clearTimer();
      if (state === 'pinned') {
        state = 'closed';
      } else {
        state = 'pinned';
      }
      notify();
      return getState();
    };

    const forceClose = () => {
      clearTimer();
      if (state !== 'closed') {
        state = 'closed';
        notify();
      }
      return getState();
    };

    const destroy = () => {
      clearTimer();
    };

    return {
      mouseEnter,
      mouseLeave,
      toggleClick,
      forceClose,
      getState,
      destroy,
    };
  }

  /**
   * Computes full layout, preview items, connected panel bounds, and hit-test bounding box
   * for a single drawer instance.
   */
  function computeDrawerLayout(
    drawer,
    {
      isOpen = false,
      isPinned = false,
      scale = 'medium',
      screenWidth = 1920,
      screenHeight = 1080,
    } = {}
  ) {
    const metrics = getScaleMetrics(scale);
    const items = Array.isArray(drawer.items) ? drawer.items : [];
    const previewItems = items.slice(0, 4);

    const direction = computeOpenDirection({
      x: drawer.x,
      y: drawer.y,
      iconCount: items.length,
      scale,
      screenWidth,
      screenHeight,
      preferredDirection: drawer.preferredDirection || 'auto',
    });

    const grid = computeDrawerGrid(items.length, direction, scale);
    const labelExtraWidth = 20;
    const labelHeight = 28;
    const panelGap = 8;

    const baseRect = {
      x: drawer.x - labelExtraWidth / 2,
      y: drawer.y,
      width: metrics.drawerSize + labelExtraWidth,
      height: metrics.drawerSize + labelHeight,
    };

    let panelRect;
    if (direction === 'right') {
      panelRect = {
        x: drawer.x + metrics.drawerSize + panelGap,
        y: drawer.y,
        width: grid.width,
        height: grid.height,
      };
    } else if (direction === 'left') {
      panelRect = {
        x: drawer.x - grid.width - panelGap,
        y: drawer.y,
        width: grid.width,
        height: grid.height,
      };
    } else {
      // 'down' - starts below the drawer handle and its label
      panelRect = {
        x: drawer.x,
        y: drawer.y + metrics.drawerSize + labelHeight + 2,
        width: grid.width,
        height: grid.height,
      };
    }

    let interactiveRect = { ...baseRect };
    if (isOpen) {
      const minX = Math.min(baseRect.x, panelRect.x);
      const minY = Math.min(baseRect.y, panelRect.y);
      const maxX = Math.max(
        baseRect.x + baseRect.width,
        panelRect.x + panelRect.width
      );
      const maxY = Math.max(
        baseRect.y + baseRect.height,
        panelRect.y + panelRect.height
      );
      interactiveRect = {
        x: minX,
        y: minY,
        width: maxX - minX,
        height: maxY - minY,
      };
    }

    return {
      metrics,
      previewItems,
      direction,
      grid,
      panelRect,
      interactiveRect,
      isOpen,
      isPinned,
    };
  }

  return {
    getScaleMetrics,
    computeDrawerGrid,
    computeOpenDirection,
    createInteractionMachine,
    computeDrawerLayout,
  };
});
