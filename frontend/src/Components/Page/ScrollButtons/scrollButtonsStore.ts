import { createPersist } from 'Helpers/createPersist';

export interface ScrollButtonsState {
  showScrollButtons: boolean;
  splitScrollButtons: boolean;
  autoHideScrollButtons: boolean;
}

const scrollButtonsStore = createPersist<ScrollButtonsState>(
  'scroll_buttons',
  () => ({
    showScrollButtons: true,
    splitScrollButtons: false,
    autoHideScrollButtons: true,
  })
);

export const useScrollButtonsOptions = () => {
  return scrollButtonsStore((state) => state);
};

export const setScrollButtonsOptions = (options: ScrollButtonsState) => {
  scrollButtonsStore.setState(options);
};
