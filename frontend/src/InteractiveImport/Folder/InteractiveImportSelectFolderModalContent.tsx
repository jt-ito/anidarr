import React, { useCallback, useMemo, useState } from 'react';
import CommandNames from 'Commands/CommandNames';
import { useExecuteCommand } from 'Commands/useCommands';
import PathInput from 'Components/Form/PathInput';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import { icons, kinds, sizes } from 'Helpers/Props';
import {
  addPinnedPath,
  addRecentFolder,
  clearActivePinnedPath,
  removePinnedPath,
  setActivePinnedPath,
  useActivePinnedPathId,
  useFavoriteFolders,
  usePinnedPaths,
  useRecentFolders,
} from 'InteractiveImport/interactiveImportFoldersStore';
import translate from 'Utilities/String/translate';
import FavoriteFolderRow from './FavoriteFolderRow';
import PinnedPathRow from './PinnedPathRow';
import RecentFolderRow from './RecentFolderRow';
import styles from './InteractiveImportSelectFolderModalContent.css';

const pinnedPathsColumns: Column[] = [
  {
    name: 'label',
    label: () => translate('Label'),
    isVisible: true,
  },
  {
    name: 'folder',
    label: () => translate('Folder'),
    isVisible: true,
  },
  {
    name: 'actions',
    label: '',
    isVisible: true,
  },
];

const favoriteFoldersColumns: Column[] = [
  {
    name: 'folder',
    label: () => translate('Folder'),
    isVisible: true,
  },
  {
    name: 'actions',
    label: '',
    isVisible: true,
  },
];

const recentFoldersColumns: Column[] = [
  {
    name: 'folder',
    label: () => translate('Folder'),
    isVisible: true,
  },
  {
    name: 'lastUsed',
    label: () => translate('LastUsed'),
    isVisible: true,
  },
  {
    name: 'actions',
    label: '',
    isVisible: true,
  },
];

interface InteractiveImportSelectFolderModalContentProps {
  modalTitle: string;
  initialFolder?: string;
  onFolderSelect(folder: string): void;
  onModalClose(): void;
}

function InteractiveImportSelectFolderModalContent(
  props: InteractiveImportSelectFolderModalContentProps
) {
  const { modalTitle, initialFolder, onFolderSelect, onModalClose } = props;
  const [folder, setFolder] = useState(initialFolder || '');
  const executeCommand = useExecuteCommand();

  const favoriteFolders = useFavoriteFolders();
  const recentFolders = useRecentFolders();
  const pinnedPaths = usePinnedPaths();
  const activePinnedPathId = useActivePinnedPathId();

  const favoriteFolderMap = useMemo(() => {
    return new Map(favoriteFolders.map((f) => [f.folder, f]));
  }, [favoriteFolders]);

  const onPathChange = useCallback(
    ({ value }: { value: string }) => {
      setFolder(value);
    },
    [setFolder]
  );

  const onRecentPathPress = useCallback(
    (value: string) => {
      setFolder(value);
    },
    [setFolder]
  );

  const onQuickImportPress = useCallback(() => {
    addRecentFolder(folder);

    executeCommand({
      name: CommandNames.DownloadedEpisodesScan,
      path: folder,
    });

    onModalClose();
  }, [folder, onModalClose, executeCommand]);

  const onInteractiveImportPress = useCallback(() => {
    addRecentFolder(folder);
    onFolderSelect(folder);
  }, [folder, onFolderSelect]);

  const onPinPathPress = useCallback(() => {
    if (folder) {
      const parts = folder.split(/[/\\]/).filter(Boolean);
      const label = parts.length > 0 ? parts[parts.length - 1] : folder;
      addPinnedPath(label, folder);
    }
  }, [folder]);

  const onSetActivePinnedPath = useCallback(
    (id: string) => {
      if (activePinnedPathId === id) {
        clearActivePinnedPath();
      } else {
        setActivePinnedPath(id);
      }
    },
    [activePinnedPathId]
  );

  const onRemovePinnedPath = useCallback((id: string) => {
    removePinnedPath(id);
  }, []);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {translate('SelectFolderModalTitle', { modalTitle })}
      </ModalHeader>

      <ModalBody>
        <PathInput
          name="folder"
          value={folder}
          includeFiles={false}
          onChange={onPathChange}
        />

        {pinnedPaths.length ? (
          <div className={styles.foldersContainer}>
            <div className={styles.foldersTitle}>
              {translate('PinnedPaths')}
            </div>

            <Table columns={pinnedPathsColumns}>
              <TableBody>
                {pinnedPaths.map((pinnedPath) => {
                  return (
                    <PinnedPathRow
                      key={pinnedPath.id}
                      id={pinnedPath.id}
                      label={pinnedPath.label}
                      folder={pinnedPath.path}
                      isActive={activePinnedPathId === pinnedPath.id}
                      onPress={onRecentPathPress}
                      onSetActive={onSetActivePinnedPath}
                      onRemove={onRemovePinnedPath}
                    />
                  );
                })}
              </TableBody>
            </Table>
          </div>
        ) : null}

        {favoriteFolders.length ? (
          <div className={styles.foldersContainer}>
            <div className={styles.foldersTitle}>
              {translate('FavoriteFolders')}
            </div>

            <Table columns={favoriteFoldersColumns}>
              <TableBody>
                {favoriteFolders.map((favoriteFolder) => {
                  return (
                    <FavoriteFolderRow
                      key={favoriteFolder.folder}
                      folder={favoriteFolder.folder}
                      onPress={onRecentPathPress}
                    />
                  );
                })}
              </TableBody>
            </Table>
          </div>
        ) : null}

        {recentFolders.length ? (
          <div className={styles.foldersContainer}>
            <div className={styles.foldersTitle}>
              {translate('RecentFolders')}
            </div>

            <Table columns={recentFoldersColumns}>
              <TableBody>
                {recentFolders
                  .slice(0)
                  .reverse()
                  .map((recentFolder) => {
                    return (
                      <RecentFolderRow
                        key={recentFolder.folder}
                        folder={recentFolder.folder}
                        lastUsed={recentFolder.lastUsed}
                        isFavorite={favoriteFolderMap.has(recentFolder.folder)}
                        onPress={onRecentPathPress}
                      />
                    );
                  })}
              </TableBody>
            </Table>
          </div>
        ) : null}

        <div className={styles.buttonsContainer}>
          <div className={styles.buttonContainer}>
            <Button
              className={styles.button}
              kind={kinds.PRIMARY}
              size={sizes.LARGE}
              isDisabled={!folder}
              onPress={onQuickImportPress}
            >
              <Icon
                className={styles.buttonIcon}
                name={icons.QUICK}
                fixedWidth={true}
              />
              {translate('MoveAutomatically')}
            </Button>
          </div>

          <div className={styles.buttonContainer}>
            <Button
              className={styles.button}
              kind={kinds.PRIMARY}
              size={sizes.LARGE}
              isDisabled={!folder}
              onPress={onInteractiveImportPress}
            >
              <Icon
                className={styles.buttonIcon}
                name={icons.INTERACTIVE}
                fixedWidth={true}
              />
              {translate('InteractiveImport')}
            </Button>
          </div>

          <div className={styles.buttonContainer}>
            <Button
              className={styles.button}
              kind={kinds.DEFAULT}
              size={sizes.LARGE}
              isDisabled={!folder}
              onPress={onPinPathPress}
            >
              <Icon
                className={styles.buttonIcon}
                name={icons.HEART}
                fixedWidth={true}
              />
              {translate('PinPath') || 'Pin Path'}
            </Button>
          </div>
        </div>
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>{translate('Cancel')}</Button>
      </ModalFooter>
    </ModalContent>
  );
}

export default InteractiveImportSelectFolderModalContent;
