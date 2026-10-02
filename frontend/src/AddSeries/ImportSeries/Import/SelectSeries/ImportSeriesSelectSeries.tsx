import {
  autoUpdate,
  flip,
  FloatingPortal,
  useClick,
  useDismiss,
  useFloating,
  useInteractions,
} from '@floating-ui/react';
import { useQueryClient } from '@tanstack/react-query';
/* eslint-disable react/jsx-no-bind */
import React, { useCallback, useEffect, useRef, useState } from 'react';
import { rememberImportChoice } from 'AddSeries/AddNewSeries/useAddSeries';
import AddSeries from 'AddSeries/AddSeries';
import FormInputButton from 'Components/Form/FormInputButton';
import TextInput from 'Components/Form/TextInput';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import useDebounce from 'Helpers/Hooks/useDebounce';
import { icons, kinds } from 'Helpers/Props';
import useExistingSeries from 'Series/useExistingSeries';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import {
  updateImportSeriesItem,
  useImportSeriesItem,
  useIsCurrentedItemQueued,
} from '../importSeriesStore';
import lookupFolder from '../lookupFolder';
import ImportSeriesSearchResult from './ImportSeriesSearchResult';
import ImportSeriesTitle from './ImportSeriesTitle';
import styles from './ImportSeriesSelectSeries.css';

const NO_RESULTS: AddSeries[] = [];

interface ImportSeriesSelectSeriesProps {
  id: string;
  onInputChange: (input: InputChanged) => void;
}

function ImportSeriesSelectSeries({
  id,
  onInputChange,
}: ImportSeriesSelectSeriesProps) {
  const importSeriesItem = useImportSeriesItem(id);
  const {
    selectedSeries,
    name,
    displayTerm,
    hasSearched,
    matchStatus,
    matchReason,
    isSearching,
    importState,
    importMessage,
    searchResults = NO_RESULTS,
    searchError,
  } = importSeriesItem ?? {};
  const isExistingSeries = !!useExistingSeries(selectedSeries);

  const [term, setTerm] = useState(displayTerm ?? name);
  const [isOpen, setIsOpen] = useState(false);
  const [contextMenu, setContextMenu] = useState<{
    x: number;
    y: number;
  } | null>(null);

  const query = useDebounce(term, term ? 300 : 0);
  const queryClient = useQueryClient();
  const isQueued = useIsCurrentedItemQueued(id);
  const isBusy = isQueued || !!isSearching;
  const isEdited = useRef(false);

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

  const handlePress = useCallback(() => {
    setIsOpen((prevIsOpen) => !prevIsOpen);
  }, []);

  const handleSearchInputChange = useCallback(
    ({ value }: InputChanged<string>) => {
      setTerm(value);
      isEdited.current = true;
    },
    []
  );

  // Searches the user asks for run straight away, ahead of the background queue
  const handleRefreshPress = useCallback(() => {
    updateImportSeriesItem({ id, searchProvider: undefined });
    lookupFolder(queryClient, id, false);
  }, [id, queryClient]);

  const handleAniDbScan = useCallback(() => {
    updateImportSeriesItem({ id, searchProvider: 'anidb' });
    lookupFolder(queryClient, id, false);
    setContextMenu(null);
  }, [id, queryClient]);

  const handleSeriesSelect = useCallback(
    (index: number) => {
      setIsOpen(false);

      const selectedSeries = searchResults[index];

      updateImportSeriesItem({
        id,
        selectedSeries,
        matchStatus: 'matched',
        importState: undefined,
        importMessage: undefined,
      });

      // The user decided: remember it for this folder name
      if (name) {
        rememberImportChoice(name, selectedSeries);
      }

      if (selectedSeries.seriesType !== 'standard') {
        onInputChange({
          name: 'seriesType',
          value: selectedSeries.seriesType,
        });
      }
    },
    [id, name, searchResults, onInputChange]
  );

  // Follow the cleaned folder name, unless the user is typing their own search
  useEffect(() => {
    if (!isEdited.current) {
      setTerm(displayTerm ?? name);
    }
  }, [displayTerm, name]);

  // Queue a lookup once the user has finished typing
  useEffect(() => {
    if (!isEdited.current) {
      return;
    }

    isEdited.current = false;
    updateImportSeriesItem({
      id,
      searchTerm: query,
      searchProvider: undefined,
    });
    lookupFolder(queryClient, id, false);
  }, [id, query, queryClient]);

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
          {isBusy && !hasSearched ? (
            <LoadingIndicator className={styles.loading} size={20} />
          ) : null}

          {hasSearched && selectedSeries && isExistingSeries ? (
            <Icon
              className={styles.warningIcon}
              name={icons.WARNING}
              kind={kinds.WARNING}
            />
          ) : null}

          {hasSearched && selectedSeries ? (
            <ImportSeriesTitle
              title={selectedSeries.title}
              year={selectedSeries.year}
              network={selectedSeries.network}
              isExistingSeries={isExistingSeries}
            />
          ) : null}

          {hasSearched && !selectedSeries ? (
            <div className={styles.status} title={matchReason}>
              <Icon
                className={styles.warningIcon}
                name={icons.WARNING}
                kind={kinds.WARNING}
              />

              <span className={styles.statusText}>
                {translate(
                  matchStatus === 'possible'
                    ? 'PossibleMatchesFound'
                    : 'NoMatchFound'
                )}
              </span>
            </div>
          ) : null}

          {!isBusy && !!searchError ? (
            <div>
              <Icon
                className={styles.warningIcon}
                title={searchError}
                name={icons.WARNING}
                kind={kinds.WARNING}
              />

              {translate('SearchFailedError')}
            </div>
          ) : null}

          {importMessage ? (
            <Icon
              className={styles.warningIcon}
              title={importMessage}
              name={icons.WARNING}
              kind={importState === 'failed' ? kinds.DANGER : kinds.WARNING}
            />
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
                      isSpinning={isBusy}
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
                  {searchResults.map((item, index) => {
                    // tvdbId and aniDbId can collide numerically, so key on both
                    const key = `${item.tvdbId || 0}:${
                      item.aniDbId || 0
                    }:${index}`;
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
                        images={item.images}
                        seasons={item.seasons}
                        overview={item.overview}
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
