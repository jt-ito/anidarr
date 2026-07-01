import React from 'react';
import { useExecuteCommand } from 'Commands/useCommands';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { inputTypes } from 'Helpers/Props';
import SettingsToolbar from 'Settings/SettingsToolbar';
import translate from 'Utilities/String/translate';
import TheTvdb from './TheTvdb';
import { useManageMetadataSourceSettings } from './useMetadataSourceSettings';

function MetadataSourceSettings() {
  const {
    settings,
    isSettingsLoading,
    handleInputChange,
    saveMetadataSourceSettings,
    isSaving,
    hasPendingChanges,
  } = useManageMetadataSourceSettings();

  const executeCommand = useExecuteCommand();

  return (
    <PageContent title={translate('MetadataSourceSettings')}>
      <SettingsToolbar
        isSaving={isSaving}
        hasPendingChanges={hasPendingChanges}
        onSavePress={saveMetadataSourceSettings}
      />

      <PageContentBody>
        <TheTvdb />

        {isSettingsLoading ? (
          <LoadingIndicator />
        ) : (
          <Form>
            <FieldSet legend={translate('MetadataProviderKeys')}>
              <FormGroup>
                <FormLabel>{translate('TvdbApiKey')}</FormLabel>
                <FormInputGroup
                  type={inputTypes.TEXT}
                  name="tvdbApiKey"
                  {...settings.tvdbApiKey}
                  helpText={translate('TvdbApiKeyHelpText')}
                  onChange={handleInputChange}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>{translate('SimklClientId')}</FormLabel>
                <FormInputGroup
                  type={inputTypes.TEXT}
                  name="simklClientId"
                  {...settings.simklClientId}
                  helpText={translate('SimklClientIdHelpText')}
                  onChange={handleInputChange}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>{translate('MalClientId')}</FormLabel>
                <FormInputGroup
                  type={inputTypes.TEXT}
                  name="malClientId"
                  {...settings.malClientId}
                  helpText={translate('MalClientIdHelpText')}
                  onChange={handleInputChange}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>{translate('AniDbClientName')}</FormLabel>
                <FormInputGroup
                  type={inputTypes.TEXT}
                  name="aniDbClientName"
                  {...settings.aniDbClientName}
                  helpText={translate('AniDbClientNameHelpText')}
                  onChange={handleInputChange}
                />
                <FormInputGroup
                  type={inputTypes.NUMBER}
                  name="aniDbClientVersion"
                  {...settings.aniDbClientVersion}
                  helpText={translate('AniDbClientVersionHelpText')}
                  onChange={handleInputChange}
                />

                <div style={{ marginTop: '10px' }}>
                  <Button
                    title="Fetch AniDB Dump"
                    /* eslint-disable-next-line react/jsx-no-bind */
                    onClick={() => executeCommand({ name: 'FetchAniDbDump' })}
                  >
                    Fetch AniDB Dump
                  </Button>
                  <p
                    style={{
                      marginTop: '5px',
                      fontSize: '0.85em',
                      color: '#888',
                    }}
                  >
                    We will use this client name to securely fetch metadata,
                    which is cached locally. None of your personal information
                    is stored or transmitted.
                  </p>
                </div>
              </FormGroup>
            </FieldSet>
          </Form>
        )}
      </PageContentBody>
    </PageContent>
  );
}

export default MetadataSourceSettings;
