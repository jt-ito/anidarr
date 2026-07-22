import React, { useCallback, useEffect, useState, useMemo } from 'react';
import Modal from 'Components/Modal/Modal';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import InteractiveImportSelectFolderModalContent from './Folder/InteractiveImportSelectFolderModalContent';
import {
  useActivePinnedPathId,
  usePinnedPaths,
  useInteractiveImportFolders,
} from './interactiveImportFoldersStore';
import InteractiveImportModalContent, {
  InteractiveImportModalContentProps,
} from './Interactive/InteractiveImportModalContent';
import { useUiSettingsValues, useSaveUiSettings } from 'Settings/UI/useUiSettings';

function InteractiveImportFoldersSync() {
  const saveSettings = useSaveUiSettings();
  const settings = useUiSettingsValues();
  const hydrate = useInteractiveImportFolders((state: any) => state.hydrate);
  const storeState = useInteractiveImportFolders();
  const [isInitialized, setIsInitialized] = useState(false);

  useEffect(() => {
    if (settings && !isInitialized) {
      if (settings.interactiveImportFolders) {
        try {
          const parsed = JSON.parse(settings.interactiveImportFolders);
          hydrate(parsed);
        } catch (e) {
          // ignore parsing error
        }
      } else {
        const localData = localStorage.getItem('interactive_import_folders');
        if (localData) {
          try {
            const parsed = JSON.parse(localData);
            if (parsed?.state) {
              hydrate(parsed.state);
              saveSettings({ interactiveImportFolders: JSON.stringify(parsed.state) });
            }
            localStorage.removeItem('interactive_import_folders');
          } catch (e) {
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
  extends Omit<InteractiveImportModalContentProps, 'modalTitle' | 'onBackToFolderSelect'> {
  isOpen: boolean;
  folder?: string;
  downloadIds?: string[];
  seriesId?: number;
  seasonNumber?: number;
  episodeId?: number;
  modalTitle?: string;
  onModalClose(): void;
}

function InteractiveImportModal(props: InteractiveImportModalProps) {
  const {
    isOpen,
    folder,
    downloadIds,
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
          modalTitle={modalTitle}
          onModalClose={onModalClose}
          onBackToFolderSelect={onBackToFolderSelect}
        />
      ) : (
        <InteractiveImportSelectFolderModalContent
          {...otherProps}
          modalTitle={modalTitle}
          initialFolder={!explicitlyClearedFolder ? activePinnedPath : undefined}
          onFolderSelect={onFolderSelect}
          onModalClose={onModalClose}
        />
      )}
    </Modal>
  );
}

export default InteractiveImportModal;
