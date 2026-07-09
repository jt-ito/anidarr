import React, { useCallback, useEffect, useMemo, useState } from 'react';
import SeriesMonitorNewItemsOptionsPopoverContent from 'AddSeries/SeriesMonitorNewItemsOptionsPopoverContent';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputButton from 'Components/Form/FormInputButton';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import Popover from 'Components/Tooltip/Popover';
import { usePendingChangesStore } from 'Helpers/Hooks/usePendingChangesStore';
import usePrevious from 'Helpers/Hooks/usePrevious';
import {
  icons,
  inputTypes,
  kinds,
  sizes,
  tooltipPositions,
} from 'Helpers/Props';
import Series from 'Series/Series';
import { useSaveSeries, useSingleSeries } from 'Series/useSeries';
import selectSettings from 'Store/Selectors/selectSettings';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import RootFolderModal from './RootFolder/RootFolderModal';
import { RootFolderUpdated } from './RootFolder/RootFolderModalContent';
import RootFolderActionModal, {
  RootFolderAction,
} from './RootFolderActionModal';
import styles from './EditSeriesModalContent.css';

export interface EditSeriesModalContentProps {
  seriesId: number;
  onModalClose: () => void;
  onDeleteSeriesPress: () => void;
}

function EditSeriesModalContent({
  seriesId,
  onModalClose,
  onDeleteSeriesPress,
}: EditSeriesModalContentProps) {
  const series = useSingleSeries(seriesId);

  if (!series) {
    return <LoadingIndicator />;
  }

  const {
    title,
    monitored,
    monitorNewItems,
    seasonFolder,
    qualityProfileId,
    seriesType,
    path,
    tags,
    rootFolderPath: initialRootFolderPath,
  } = series;

  const { pendingChanges, setPendingChange } = usePendingChangesStore<Series>(
    {}
  );

  const [isRootFolderModalOpen, setIsRootFolderModalOpen] = useState(false);
  const [rootFolderPath, setRootFolderPath] = useState(initialRootFolderPath);
  const isPathChanging = !!(
    pendingChanges.path && path !== pendingChanges.path
  );
  // Anidarr: replace MoveSeriesModal with three-option RootFolderActionModal
  const [isRootFolderActionModalOpen, setIsRootFolderActionModalOpen] =
    useState(false);

  const { saveSeries, isSaving, saveError } = useSaveSeries(isPathChanging);
  const wasSaving = usePrevious(isSaving);

  const { settings, ...otherSettings } = useMemo(() => {
    return selectSettings(
      {
        monitored,
        monitorNewItems,
        seasonFolder,
        qualityProfileId,
        seriesType,
        path,
        tags,
      },
      pendingChanges,
      saveError
    );
  }, [
    monitored,
    monitorNewItems,
    seasonFolder,
    qualityProfileId,
    seriesType,
    path,
    tags,
    pendingChanges,
    saveError,
  ]);

  const handleInputChange = useCallback(
    ({ name, value }: InputChanged) => {
      // @ts-expect-error name needs to be keyof Series
      setPendingChange(name, value);
    },
    [setPendingChange]
  );

  const handleRootFolderPress = useCallback(() => {
    setIsRootFolderModalOpen(true);
  }, []);

  const handleRootFolderModalClose = useCallback(() => {
    setIsRootFolderModalOpen(false);
  }, []);

  const handleRootFolderChange = useCallback(
    ({
      path: newPath,
      rootFolderPath: newRootFolderPath,
    }: RootFolderUpdated) => {
      setIsRootFolderModalOpen(false);
      setRootFolderPath(newRootFolderPath);
      handleInputChange({ name: 'path', value: newPath });
    },
    [handleInputChange]
  );

  const handleCancelPress = useCallback(() => {
    setIsRootFolderActionModalOpen(false);
  }, []);

  const handleSavePress = useCallback(() => {
    if (isPathChanging && !isRootFolderActionModalOpen) {
      // Anidarr: show the three-option modal instead of the old two-option MoveSeriesModal
      setIsRootFolderActionModalOpen(true);
    } else {
      setIsRootFolderActionModalOpen(false);
      saveSeries({
        ...series,
        ...pendingChanges,
      });
    }
  }, [
    series,
    isPathChanging,
    isRootFolderActionModalOpen,
    pendingChanges,
    saveSeries,
  ]);

  const handleRootFolderActionConfirm = useCallback(
    (action: RootFolderAction) => {
      setIsRootFolderActionModalOpen(false);
      saveSeries({
        ...series,
        ...pendingChanges,
        // Send the chosen action to the API
        // @ts-expect-error rootFolderAction is an Anidarr extension on the Series type
        rootFolderAction: action,
      });
    },
    [series, pendingChanges, saveSeries]
  );

  useEffect(() => {
    if (!isSaving && wasSaving && !saveError) {
      onModalClose();
    }
  }, [isSaving, wasSaving, saveError, onModalClose]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('EditSeriesModalHeader', { title })}</ModalHeader>

      <ModalBody>
        <Form {...otherSettings}>
          <FormGroup size={sizes.MEDIUM}>
            <FormLabel>{translate('Monitored')}</FormLabel>

            <FormInputGroup
              type={inputTypes.CHECK}
              name="monitored"
              helpText={translate('MonitoredEpisodesHelpText')}
              {...settings.monitored}
              onChange={handleInputChange}
            />
          </FormGroup>

          <FormGroup size={sizes.MEDIUM}>
            <FormLabel>
              {translate('MonitorNewSeasons')}
              <Popover
                anchor={<Icon className={styles.labelIcon} name={icons.INFO} />}
                title={translate('MonitorNewSeasons')}
                body={<SeriesMonitorNewItemsOptionsPopoverContent />}
                position={tooltipPositions.RIGHT}
              />
            </FormLabel>

            <FormInputGroup
              type={inputTypes.MONITOR_NEW_ITEMS_SELECT}
              name="monitorNewItems"
              helpText={translate('MonitorNewSeasonsHelpText')}
              {...settings.monitorNewItems}
              onChange={handleInputChange}
            />
          </FormGroup>

          <FormGroup size={sizes.MEDIUM}>
            <FormLabel>{translate('UseSeasonFolder')}</FormLabel>

            <FormInputGroup
              type={inputTypes.CHECK}
              name="seasonFolder"
              helpText={translate('UseSeasonFolderHelpText')}
              {...settings.seasonFolder}
              onChange={handleInputChange}
            />
          </FormGroup>

          <FormGroup size={sizes.MEDIUM}>
            <FormLabel>{translate('QualityProfile')}</FormLabel>

            <FormInputGroup
              type={inputTypes.QUALITY_PROFILE_SELECT}
              name="qualityProfileId"
              {...settings.qualityProfileId}
              onChange={handleInputChange}
            />
          </FormGroup>

          <FormGroup size={sizes.MEDIUM}>
            <FormLabel>{translate('SeriesType')}</FormLabel>

            <FormInputGroup
              type={inputTypes.SERIES_TYPE_SELECT}
              name="seriesType"
              {...settings.seriesType}
              helpText={translate('SeriesTypesHelpText')}
              onChange={handleInputChange}
            />
          </FormGroup>

          <FormGroup size={sizes.MEDIUM}>
            <FormLabel>{translate('Path')}</FormLabel>

            <FormInputGroup
              type={inputTypes.PATH}
              name="path"
              {...settings.path}
              buttons={[
                <FormInputButton
                  key="fileBrowser"
                  kind={kinds.DEFAULT}
                  title={translate('RootFolder')}
                  onPress={handleRootFolderPress}
                >
                  <Icon name={icons.ROOT_FOLDER} />
                </FormInputButton>,
              ]}
              includeFiles={false}
              onChange={handleInputChange}
            />
          </FormGroup>

          <FormGroup size={sizes.MEDIUM}>
            <FormLabel>{translate('Tags')}</FormLabel>

            <FormInputGroup
              type={inputTypes.TAG}
              name="tags"
              {...settings.tags}
              onChange={handleInputChange}
            />
          </FormGroup>
        </Form>
      </ModalBody>

      <ModalFooter>
        <Button
          className={styles.deleteButton}
          kind={kinds.DANGER}
          onPress={onDeleteSeriesPress}
        >
          {translate('Delete')}
        </Button>

        <Button onPress={onModalClose}>{translate('Cancel')}</Button>

        <SpinnerErrorButton
          error={saveError}
          isSpinning={isSaving}
          onPress={handleSavePress}
        >
          {translate('Save')}
        </SpinnerErrorButton>
      </ModalFooter>

      <RootFolderModal
        isOpen={isRootFolderModalOpen}
        seriesId={seriesId}
        rootFolderPath={rootFolderPath}
        onSavePress={handleRootFolderChange}
        onModalClose={handleRootFolderModalClose}
      />

      {/* Anidarr: three-option modal replaces MoveSeriesModal */}
      <RootFolderActionModal
        isOpen={isRootFolderActionModalOpen}
        originalPath={path}
        newPath={pendingChanges.path}
        onConfirm={handleRootFolderActionConfirm}
        onModalClose={handleCancelPress}
      />
    </ModalContent>
  );
}

export default EditSeriesModalContent;
