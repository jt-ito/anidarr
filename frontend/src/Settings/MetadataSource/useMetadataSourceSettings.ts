import { useCallback } from 'react';
import { useManageSettings } from 'Settings/useSettings';
import { InputChanged } from 'typings/inputs';

export interface MetadataSourceSettings {
  id: number;
  tvdbApiKey: string;
  simklClientId: string;
  malClientId: string;
  aniDbClientName: string;
  aniDbClientVersion: number;
}

const SETTINGS_PATH = '/settings/metadatasource';

export const useManageMetadataSourceSettings = () => {
  const {
    settings,
    isFetching,
    error,
    saveSettings,
    isSaving,
    saveError,
    updateSetting,
    hasPendingChanges,
  } = useManageSettings<MetadataSourceSettings>(SETTINGS_PATH);

  const handleInputChange = useCallback(
    (change: InputChanged) => {
      // @ts-expect-error input change events aren't typed
      updateSetting(change.name, change.value);
    },
    [updateSetting]
  );

  return {
    settings,
    isSettingsLoading: isFetching,
    settingsError: error,
    handleInputChange,
    saveMetadataSourceSettings: saveSettings,
    isSaving,
    saveError,
    hasPendingChanges,
  };
};
