import React, { useCallback, useEffect, useMemo, useState } from 'react';
import Modal from 'Components/Modal/Modal';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { sizes } from 'Helpers/Props';
import {
  useSaveUiSettings,
  useUiSettingsValues,
} from 'Settings/UI/useUiSettings';
import translate from 'Utilities/String/translate';
import InteractiveImportSelectFolderModalContent from './Folder/InteractiveImportSelectFolderModalContent';
import InteractiveImportModalContent, {
  InteractiveImportModalContentProps,
} from './Interactive/InteractiveImportModalContent';
import {
  useActivePinnedPathId,
  useInteractiveImportFolders,
  usePinnedPaths,
} from './interactiveImportFoldersStore';

function InteractiveImportFoldersSync() {
  const saveSettings = useSaveUiSettings();
  const settings = useUiSettingsValues();
  const hydrate = useInteractiveImportFolders().hydrate;
  const storeState = useInteractiveImportFolders();
  const [isInitialized, setIsInitialized] = useState(false);

  useEffect(() => {
    if (settings && !isInitialized) {
      if (settings.interactiveImportFolders) {
        try {
          const parsed = JSON.parse(settings.interactiveImportFolders);
          hydrate(parsed);
        } catch (_e) {
          // ignore parsing error
        }
      } else {
        const localData = localStorage.getItem('interactive_import_folders');

        if (localData) {
          try {
            const parsed = JSON.parse(localData);

            if (parsed?.state) {
              hydrate(parsed.state);
              saveSettings({
                interactiveImportFolders: JSON.stringify(parsed.state),
              });
            }

            localStorage.removeItem('interactive_import_folders');
          } catch (_e) {
            // ignore
          }
        }
      }

      setIsInitialized(true);
    }
  }, [settings, isInitialized, hydrate, saveSettings]);

  useEffect(() => {
    if (isInitialized) {
      const serialized = JSON.stringify({
        recentFolders: storeState.recentFolders,
        favoriteFolders: storeState.favoriteFolders,
        pinnedPaths: storeState.pinnedPaths,
        activePinnedPathId: storeState.activePinnedPathId,
      });

      if (settings?.interactiveImportFolders !== serialized) {
        saveSettings({ interactiveImportFolders: serialized });
      }
    }
  }, [
    storeState.recentFolders,
    storeState.favoriteFolders,
    storeState.pinnedPaths,
    storeState.activePinnedPathId,
    isInitialized,
    settings?.interactiveImportFolders,
    saveSettings,
  ]);

  return null;
}

interface InteractiveImportModalProps
  extends Omit<
    InteractiveImportModalContentProps,
    'modalTitle' | 'onBackToFolderSelect'
  > {
  isOpen: boolean;
  folder?: string;
  downloadIds?: string[];
  seriesId?: number;
  seasonNumber?: number;
  episodeId?: number;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  episodeEntity?: any; // avoid adding full Episode import if possible, but actually we can import it or use any.
  modalTitle?: string;
  onModalClose(): void;
}

function InteractiveImportModal(props: InteractiveImportModalProps) {
  const {
    isOpen,
    folder,
    downloadIds,
    seasonNumber,
    episodeId,
    episodeEntity,
    modalTitle = translate('ManualImport'),
    onModalClose,
    ...otherProps
  } = props;

  const [folderPath, setFolderPath] = useState<string | undefined>(folder);
  const [explicitlyClearedFolder, setExplicitlyClearedFolder] = useState(false);
  const previousIsOpen = usePrevious(isOpen);

  const activePinnedPathId = useActivePinnedPathId();
  const pinnedPaths = usePinnedPaths();

  const activePinnedPath = useMemo(() => {
    return pinnedPaths.find((p) => p.id === activePinnedPathId)?.path;
  }, [activePinnedPathId, pinnedPaths]);

  const onFolderSelect = useCallback(
    (path: string) => {
      setFolderPath(path);
      setExplicitlyClearedFolder(false);
    },
    [setFolderPath]
  );

  const onBackToFolderSelect = useCallback(() => {
    setFolderPath(undefined);
    setExplicitlyClearedFolder(true);
  }, []);

  useEffect(() => {
    setFolderPath(folder);
    setExplicitlyClearedFolder(false);
  }, [folder, setFolderPath]);

  useEffect(() => {
    if (previousIsOpen && !isOpen) {
      setFolderPath(folder);
      setExplicitlyClearedFolder(false);
    }
  }, [folder, previousIsOpen, isOpen, setFolderPath]);

  // Determine final folder to use
  const finalFolder = folderPath;

  return (
    <Modal
      isOpen={isOpen}
      size={sizes.EXTRA_EXTRA_LARGE}
      closeOnBackgroundClick={false}
      onModalClose={onModalClose}
    >
      <InteractiveImportFoldersSync />
      {finalFolder || downloadIds ? (
        <InteractiveImportModalContent
          {...otherProps}
          folder={finalFolder}
          downloadIds={downloadIds}
          episodeEntity={episodeEntity}
          modalTitle={modalTitle}
          onModalClose={onModalClose}
          onBackToFolderSelect={onBackToFolderSelect}
        />
      ) : (
        <InteractiveImportSelectFolderModalContent
          {...otherProps}
          modalTitle={modalTitle}
          initialFolder={explicitlyClearedFolder ? undefined : activePinnedPath}
          onFolderSelect={onFolderSelect}
          onModalClose={onModalClose}
        />
      )}
    </Modal>
  );
}

export default InteractiveImportModal;
