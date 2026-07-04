import React from 'react';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';

interface AniDbBannedModalProps {
  isOpen: boolean;
  onModalClose: () => void;
}

function AniDbBannedModal({ isOpen, onModalClose }: AniDbBannedModalProps) {
  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>AniDB Rate Limited</ModalHeader>

        <ModalBody>
          <div>
            Anidarr has been temporarily rate-limited by AniDB. Metadata lookups
            will be skipped until the ban expires (usually within 24 hours).
          </div>

          <div style={{ marginTop: '20px' }}>
            No action is required — Anidarr will automatically resume fetching
            metadata once the ban lifts. You can monitor status in{' '}
            <strong>System &rsaquo; Status &rsaquo; Health</strong>.
          </div>
        </ModalBody>

        <ModalFooter>
          <Button kind={kinds.PRIMARY} onPress={onModalClose}>
            OK
          </Button>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

export default AniDbBannedModal;
