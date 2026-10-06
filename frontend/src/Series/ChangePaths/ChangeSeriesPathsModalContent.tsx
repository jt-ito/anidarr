import { useQueryClient } from '@tanstack/react-query';
import React, { useCallback, useMemo, useState } from 'react';
import CommandNames from 'Commands/CommandNames';
import { useExecuteCommand } from 'Commands/useCommands';
import SelectInput from 'Components/Form/SelectInput';
import TextInput from 'Components/Form/TextInput';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { icons } from 'Helpers/Props';
import useRootFolders from 'RootFolder/useRootFolders';
import MoveSeriesModal from 'Series/MoveSeries/MoveSeriesModal';
import useSeries from 'Series/useSeries';
import { InputChanged } from 'typings/inputs';
import fetchJson from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';
import translate from 'Utilities/String/translate';
import styles from './ChangeSeriesPathsModalContent.css';

interface ChangeSeriesPathsModalContentProps {
  onModalClose: () => void;
}

const trimEnd = (path: string) => path.replace(/[\\/]+$/, '');

// Replaces the leading `from` folder of `path` with `to`, keeping the rest.
// Returns null when `path` isn't inside `from`.
const swapPrefix = (path: string, from: string, to: string) => {
  const start = trimEnd(from);

  if (path.toLowerCase() === start.toLowerCase()) {
    return trimEnd(to);
  }

  const separator = path.charAt(start.length);

  if (
    (separator !== '/' && separator !== '\\') ||
    !path.toLowerCase().startsWith(start.toLowerCase())
  ) {
    return null;
  }

  return trimEnd(to) + path.slice(start.length);
};

function ChangeSeriesPathsModalContent({
  onModalClose,
}: ChangeSeriesPathsModalContentProps) {
  const queryClient = useQueryClient();
  const executeCommand = useExecuteCommand();
  const { data: rootFolders } = useRootFolders();
  const { data: allSeries } = useSeries();

  const [fromPath, setFromPath] = useState('');
  const [toPath, setToPath] = useState('');
  const [isConfirmOpen, setIsConfirmOpen] = useState(false);
  const [status, setStatus] = useState<'idle' | 'backup' | 'saving'>('idle');
  const [error, setError] = useState('');

  const from = fromPath || rootFolders[0]?.path || '';
  const to = toPath.trim();

  const options = useMemo(
    () => rootFolders.map((r) => ({ key: r.path, value: r.path })),
    [rootFolders]
  );

  const matches = useMemo(
    () =>
      from && to ? allSeries.filter((s) => swapPrefix(s.path, from, to)) : [],
    [allSeries, from, to]
  );

  const canApply = matches.length > 0 && trimEnd(from) !== trimEnd(to);
  const isBusy = status !== 'idle';

  const apply = useCallback(
    (moveFiles: boolean) => {
      setIsConfirmOpen(false);
      setError('');
      setStatus('backup');

      executeCommand({ name: CommandNames.Backup }, async (backup) => {
        if (backup.status !== 'completed') {
          setError(translate('ChangeSeriesPathsBackupFailed'));
          setStatus('idle');
          return;
        }

        setStatus('saving');

        let failed = 0;

        // Sequential on purpose: each save can queue a move command.
        for (const series of matches) {
          try {
            await fetchJson({
              path: getQueryPath(`/series/${series.id}?moveFiles=${moveFiles}`),
              method: 'PUT',
              headers: {
                'X-Api-Key': window.Sonarr.apiKey,
                'X-Sonarr-Client': 'Sonarr',
              },
              body: { ...series, path: swapPrefix(series.path, from, to) },
            });
          } catch {
            failed++;
          }
        }

        await queryClient.invalidateQueries({ queryKey: ['/series'] });

        setStatus('idle');

        if (failed) {
          setError(
            translate('ChangeSeriesPathsFailed', {
              failed,
              count: matches.length,
            })
          );
        } else {
          onModalClose();
        }
      });
    },
    [executeCommand, matches, from, to, queryClient, onModalClose]
  );

  const onFromChange = useCallback(
    ({ value }: InputChanged<string>) => setFromPath(value),
    []
  );
  const onToChange = useCallback(
    ({ value }: InputChanged<string>) => setToPath(value),
    []
  );
  const onOkPress = useCallback(() => {
    const isRootFolder = rootFolders.some(
      (r) => trimEnd(r.path).toLowerCase() === trimEnd(to).toLowerCase()
    );

    if (!isRootFolder) {
      setError(translate('ChangeSeriesPathsNotRootFolder', { path: to }));
      return;
    }

    setError('');
    setIsConfirmOpen(true);
  }, [rootFolders, to]);
  const onConfirmClose = useCallback(() => setIsConfirmOpen(false), []);
  const onDontMove = useCallback(() => apply(false), [apply]);
  const onMove = useCallback(() => apply(true), [apply]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('ChangeSeriesPaths')}</ModalHeader>

      <ModalBody>
        <div className={styles.row}>
          <div className={styles.field}>
            <SelectInput
              name="fromPath"
              value={from}
              values={options}
              isDisabled={isBusy}
              onChange={onFromChange}
            />
          </div>

          <Icon className={styles.arrow} name={icons.ARROW_RIGHT} size={20} />

          <div className={styles.field}>
            <TextInput
              name="toPath"
              value={toPath}
              placeholder="/downloads/tv"
              readOnly={isBusy}
              onChange={onToChange}
            />
          </div>
        </div>

        <div className={styles.help}>
          {translate('ChangeSeriesPathsCount', { count: matches.length })}
        </div>

        {error ? <div className={styles.error}>{error}</div> : null}
      </ModalBody>

      <ModalFooter>
        <Button isDisabled={isBusy} onPress={onModalClose}>
          {translate('Cancel')}
        </Button>

        <SpinnerButton
          isSpinning={isBusy}
          isDisabled={!canApply}
          onPress={onOkPress}
        >
          {status === 'backup'
            ? translate('ChangeSeriesPathsBackingUp')
            : translate('ApplyChanges')}
        </SpinnerButton>
      </ModalFooter>

      <MoveSeriesModal
        isOpen={isConfirmOpen}
        originalPath={from}
        destinationPath={to}
        onModalClose={onConfirmClose}
        onSavePress={onDontMove}
        onMoveSeriesPress={onMove}
      />
    </ModalContent>
  );
}

export default ChangeSeriesPathsModalContent;
