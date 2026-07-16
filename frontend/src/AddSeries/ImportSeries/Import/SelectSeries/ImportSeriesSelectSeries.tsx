import {
  autoUpdate,
  flip,
  FloatingPortal,
  useClick,
  useDismiss,
  useFloating,
  useInteractions,
} from '@floating-ui/react';
import React, { useCallback, useEffect, useState } from 'react';
import { useLookupSeries } from 'AddSeries/AddNewSeries/useAddSeries';
import FormInputButton from 'Components/Form/FormInputButton';
import TextInput from 'Components/Form/TextInput';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import useDebounce from 'Helpers/Hooks/useDebounce';
import { icons, kinds } from 'Helpers/Props';
import useExistingSeries from 'Series/useExistingSeries';
import { InputChanged } from 'typings/inputs';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import {
  addToLookupQueue,
  removeFromLookupQueue,
  updateImportSeriesItem,
  useImportSeriesItem,
  useIsCurrentedItemQueued,
  useIsCurrentLookupQueueItem,
} from '../importSeriesStore';
import ImportSeriesSearchResult from './ImportSeriesSearchResult';
import ImportSeriesTitle from './ImportSeriesTitle';
import styles from './ImportSeriesSelectSeries.css';

interface ImportSeriesSelectSeriesProps {
  id: string;
  onInputChange: (input: InputChanged) => void;
}

function ImportSeriesSelectSeries({
  id,
  onInputChange,
}: ImportSeriesSelectSeriesProps) {
  const importSeriesItem = useImportSeriesItem(id);
  const { selectedSeries, name } = importSeriesItem ?? {};
  const isExistingSeries = !!useExistingSeries({
    tvdbId: selectedSeries?.tvdbId,
  });

  const [term, setTerm] = useState(name);
  const [isOpen, setIsOpen] = useState(false);
  const [provider, setProvider] = useState<string | undefined>(undefined);
  const [contextMenu, setContextMenu] = useState<{
    x: number;
    y: number;
  } | null>(null);

  const query = useDebounce(term, term ? 300 : 0);
  const isCurrentLookupQueueItem = useIsCurrentLookupQueueItem(id);
  const isQueued = useIsCurrentedItemQueued(id);

  useEffect(() => {
    if (!contextMenu) return;
    const handleGlobalClick = () => setContextMenu(null);

    // Defer adding the event listener so the current click doesn't trigger it
    const timeoutId = setTimeout(() => {
      document.addEventListener('click', handleGlobalClick);
    }, 0);

    return () => {
      clearTimeout(timeoutId);
      document.removeEventListener('click', handleGlobalClick);
    };
  }, [contextMenu]);

  const { isFetching, isFetched, error, data, refetch } = useLookupSeries(
    query,
    provider,
    isCurrentLookupQueueItem
  );

  const errorMessage = getErrorMessage(error);
  const isLookingUpSeries = isFetching || isQueued;

  const handlePress = useCallback(() => {
    setIsOpen((prevIsOpen) => !prevIsOpen);
  }, []);

  const handleSearchInputChange = useCallback(
    ({ value }: InputChanged<string>) => {
      setTerm(value);
      setProvider(undefined);
      addToLookupQueue(id);
    },
    [id]
  );

  const handleRefreshPress = useCallback(() => {
    if (provider === undefined) refetch();
    else setProvider(undefined);
  }, [provider, refetch]);

  const handleAniDbScan = useCallback(() => {
    if (provider === 'anidb') refetch();
    else setProvider('anidb');
    setContextMenu(null);
  }, [provider, refetch]);

  const handleSeriesSelect = useCallback(
    (index: number) => {
      setIsOpen(false);

      const selectedSeries = data[index];

      updateImportSeriesItem({
        id,
        selectedSeries,
      });

      if (selectedSeries.seriesType !== 'standard') {
        onInputChange({
          name: 'seriesType',
          value: selectedSeries.seriesType,
        });
      }
    },
    [id, data, onInputChange]
  );

  useEffect(() => {
    if (isFetched) {
      updateImportSeriesItem({
        id,
        hasSearched: isFetched,
        selectedSeries: data[0],
      });

      removeFromLookupQueue(id);
    }
  }, [id, isFetched, data]);

  useEffect(() => {
    setTerm(name);
  }, [name]);

  useEffect(() => {
    const handleGlobalClick = () => setContextMenu(null);
    document.addEventListener('click', handleGlobalClick);
    return () => document.removeEventListener('click', handleGlobalClick);
  }, []);

  const { refs, context, floatingStyles } = useFloating({
    middleware: [
      flip({
        crossAxis: false,
        mainAxis: true,
      }),
    ],
    open: isOpen,
    placement: 'bottom',
    whileElementsMounted: autoUpdate,
    onOpenChange: setIsOpen,
  });

  const click = useClick(context);
  const dismiss = useDismiss(context);

  const { getReferenceProps, getFloatingProps } = useInteractions([
    click,
    dismiss,
  ]);

  return (
    <>
      <div ref={refs.setReference} {...getReferenceProps()}>
        <Link className={styles.button} component="div" onPress={handlePress}>
          {isLookingUpSeries && isQueued && !isFetched ? (
            <LoadingIndicator className={styles.loading} size={20} />
          ) : null}

          {isFetched && selectedSeries && isExistingSeries ? (
            <Icon
              className={styles.warningIcon}
              name={icons.WARNING}
              kind={kinds.WARNING}
            />
          ) : null}

          {isFetched && selectedSeries ? (
            <ImportSeriesTitle
              title={selectedSeries.title}
              year={selectedSeries.year}
              network={selectedSeries.network}
              isExistingSeries={isExistingSeries}
            />
          ) : null}

          {isFetched && !selectedSeries ? (
            <div>
              <Icon
                className={styles.warningIcon}
                name={icons.WARNING}
                kind={kinds.WARNING}
              />

              {translate('NoMatchFound')}
            </div>
          ) : null}

          {!isFetching && !!error ? (
            <div>
              <Icon
                className={styles.warningIcon}
                title={errorMessage}
                name={icons.WARNING}
                kind={kinds.WARNING}
              />

              {translate('SearchFailedError')}
            </div>
          ) : null}

          <div className={styles.dropdownArrowContainer}>
            <Icon name={icons.CARET_DOWN} />
          </div>
        </Link>
      </div>

      {isOpen ? (
        <FloatingPortal id="portal-root">
          <div
            ref={refs.setFloating}
            className={styles.contentContainer}
            style={floatingStyles}
            {...getFloatingProps()}
          >
            {isOpen ? (
              <div className={styles.content}>
                <div className={styles.searchContainer}>
                  <div className={styles.searchIconContainer}>
                    <Icon name={icons.SEARCH} />
                  </div>

                  <TextInput
                    className={styles.searchInput}
                    name={`${name}_textInput`}
                    value={term}
                    onChange={handleSearchInputChange}
                  />

                  <div
                    onContextMenu={(e) => {
                      e.preventDefault();
                      setContextMenu({ x: e.clientX, y: e.clientY });
                    }}
                  >
                    <FormInputButton
                      kind={kinds.DEFAULT}
                      spinnerIcon={icons.REFRESH}
                      canSpin={true}
                      isSpinning={isFetching}
                      onPress={handleRefreshPress}
                    >
                      <Icon name={icons.REFRESH} />
                    </FormInputButton>
                  </div>

                  {contextMenu && (
                    <FloatingPortal>
                      <div
                        style={{
                          position: 'fixed',
                          top: Math.min(contextMenu.y, window.innerHeight - 50),
                          left: Math.min(
                            contextMenu.x,
                            window.innerWidth - 150
                          ),
                          zIndex: 99999,
                          background: 'var(--cardBackgroundColor)',
                          border: '1px solid var(--inputBorderColor)',
                          borderRadius: '4px',
                          boxShadow: '0 2px 10px rgba(0,0,0,0.5)',
                          padding: '4px 0',
                          color: 'var(--textColor)',
                        }}
                      >
                        <div
                          style={{
                            padding: '8px 16px',
                            cursor: 'pointer',
                            whiteSpace: 'nowrap',
                          }}
                          onClick={handleAniDbScan}
                          onMouseEnter={(e) =>
                            (e.currentTarget.style.backgroundColor =
                              'var(--menuItemHoverBackgroundColor)')
                          }
                          onMouseLeave={(e) =>
                            (e.currentTarget.style.backgroundColor =
                              'transparent')
                          }
                        >
                          Scan with AniDB
                        </div>
                      </div>
                    </FloatingPortal>
                  )}
                </div>

                <div className={styles.results}>
                  {data.map((item, index) => {
                    const key = item.tvdbId || item.aniDbId || index;
                    return (
                      <ImportSeriesSearchResult
                        key={key}
                        index={index}
                        tvdbId={item.tvdbId}
                        aniDbId={item.aniDbId}
                        primaryMetadataProvider={item.primaryMetadataProvider}
                        title={item.title}
                        year={item.year}
                        network={item.network}
                        onPress={handleSeriesSelect}
                      />
                    );
                  })}
                </div>
              </div>
            ) : null}
          </div>
        </FloatingPortal>
      ) : null}
    </>
  );
}

export default ImportSeriesSelectSeries;
