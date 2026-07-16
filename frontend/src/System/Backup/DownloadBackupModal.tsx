import React from 'react';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './DownloadBackupModal.css';

interface DownloadBackupModalProps {
  isOpen: boolean;
  id: number;
  path: string;
  onModalClose: () => void;
}

function DownloadBackupModal({
  isOpen,
  id,
  path,
  onModalClose,
}: DownloadBackupModalProps) {
  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <ModalContent className={styles.modalContent} onModalClose={onModalClose}>
        <ModalHeader>{translate('Download Backup')}</ModalHeader>

        <ModalBody className={styles.modalBody}>
          <div className={styles.options}>
            <a
              href={`${window.Sonarr.urlBase}${path}`}
              className={styles.linkButton}
              onClick={onModalClose}
            >
              <Button className={styles.button}>
                <Icon name={icons.DOWNLOAD} />
                <span>{translate('Download Anidarr Backup (full)')}</span>
              </Button>
            </a>

            <p className={styles.description}>
              {translate(
                'Includes all settings, series, and AniDB metadata. Use this if you are staying on Anidarr.'
              )}
            </p>

            <a
              href={`${window.Sonarr.urlBase}/api/v3/system/backup/download/sonarr-compatible/${id}`}
              className={styles.linkButton}
              onClick={onModalClose}
            >
              <Button className={styles.button}>
                <Icon name={icons.DOWNLOAD} />
                <span>{translate('Download Sonarr-Compatible Backup')}</span>
              </Button>
            </a>

            <p className={styles.description}>
              {translate(
                'For migrating back to stock Sonarr. Strips all AniDB-specific series and metadata to prevent import errors.'
              )}
            </p>
          </div>
        </ModalBody>

        <ModalFooter>
          <Button onPress={onModalClose}>{translate('Cancel')}</Button>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

export default DownloadBackupModal;
