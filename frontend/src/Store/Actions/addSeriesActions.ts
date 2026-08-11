import { createAction } from 'redux-actions';
import getSectionState from 'Utilities/State/getSectionState';
import updateSectionState from 'Utilities/State/updateSectionState';
import createHandleActions from './Creators/createHandleActions';

export const section = 'addSeries';

export const defaultState = {
  provider: '',
};

export const SET_PROVIDER = 'addSeries/setProvider';
export const RESET_ADD_SERIES = 'addSeries/reset';

export const setProvider = createAction<string>(SET_PROVIDER);
export const resetAddSeries = createAction(RESET_ADD_SERIES);

export const reducers = createHandleActions(
  {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    [SET_PROVIDER]: function (state: any, { payload }: any) {
      const newState = Object.assign(getSectionState(state, section), {
        provider: payload,
      });
      return updateSectionState(state, section, newState);
    },
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    [RESET_ADD_SERIES]: function (state: any) {
      return updateSectionState(state, section, defaultState);
    },
  },
  defaultState
);
