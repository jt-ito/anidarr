import { createPersist } from 'Helpers/createPersist';

export interface ScrollButtonsState {
  showScrollButtons: boolean;
  splitScrollButtons: boolean;
}

const scrollButtonsStore = createPersist<ScrollButtonsState>(
  'scroll_buttons',
  () => ({ showScrollButtons: true, splitScrollButtons: false })
);

export const useScrollButtonsOptions = () => {
  return scrollButtonsStore((state) => state);
};

export const setScrollButtonsOptions = (options: ScrollButtonsState) => {
  scrollButtonsStore.setState(options);
};
