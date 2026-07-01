import React, { useCallback, useState } from 'react';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds, sizes } from 'Helpers/Props';
import styles from './RootFolderActionModal.css';

export type RootFolderAction = 'MoveFiles' | 'HardlinkToNew' | 'PathUpdateOnly';

interface RootFolderActionModalProps {
  isOpen: boolean;
  originalPath?: string;
  newPath?: string;
  onConfirm: (action: RootFolderAction) => void;
  onModalClose: () => void;
}

const options: {
  value: RootFolderAction;
  label: string;
  description: string;
}[] = [
  {
    value: 'MoveFiles',
    label: 'Move files',
    description:
      'Physically move all episode files to the new folder. Original location will be empty.',
  },
  {
    value: 'HardlinkToNew',
    label: 'Hardlink to new location',
    description:
      'Create hardlinks in the new folder. Original files stay in place. Requires both locations to be on the same drive/filesystem.',
  },
  {
    value: 'PathUpdateOnly',
    label: 'Change path only',
    description:
      "Update the stored path without touching files. Use this if you've already moved or hardlinked the files manually.",
  },
];

function RootFolderActionModal({
  isOpen,
  originalPath,
  newPath,
  onConfirm,
  onModalClose,
}: RootFolderActionModalProps) {
  const [selected, setSelected] = useState<RootFolderAction>('MoveFiles');

  const handleConfirm = useCallback(() => {
    onConfirm(selected);
  }, [selected, onConfirm]);

  return (
    <Modal
      isOpen={isOpen}
      size={sizes.MEDIUM}
      closeOnBackgroundClick={false}
      onModalClose={onModalClose}
    >
      <ModalContent showCloseButton={true} onModalClose={onModalClose}>
        <ModalHeader>Root Folder Changed</ModalHeader>

        <ModalBody>
          {originalPath && newPath ? (
            <p className={styles.pathInfo}>
              Changing from <strong>{originalPath}</strong> to{' '}
              <strong>{newPath}</strong>.
            </p>
          ) : null}

          <p className={styles.question}>
            What would you like to do with the existing files?
          </p>

          <div className={styles.options}>
            {options.map((opt) => (
              <label key={opt.value} className={styles.option}>
                <input
                  type="radio"
                  name="rootFolderAction"
                  value={opt.value}
                  checked={selected === opt.value}
                  className={styles.radio}
                  /* eslint-disable-next-line react/jsx-no-bind */
                  onChange={() => setSelected(opt.value)}
                />
                <div className={styles.optionText}>
                  <span className={styles.optionLabel}>{opt.label}</span>
                  <span className={styles.optionDescription}>
                    {opt.description}
                  </span>
                </div>
              </label>
            ))}
          </div>
        </ModalBody>

        <ModalFooter>
          <Button onPress={onModalClose}>Cancel</Button>

          <Button kind={kinds.PRIMARY} onPress={handleConfirm}>
            Confirm
          </Button>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

export default RootFolderActionModal;
