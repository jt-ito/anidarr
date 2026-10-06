import React, { useCallback, useState } from 'react';
import Modal from 'Components/Modal/Modal';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import ChangeSeriesPathsModalContent from './ChangeSeriesPathsModalContent';

function ChangeSeriesPathsToolbarButton() {
  const [isOpen, setIsOpen] = useState(false);

  const onOpen = useCallback(() => setIsOpen(true), []);
  const onClose = useCallback(() => setIsOpen(false), []);

  return (
    <>
      <PageToolbarButton
        label={translate('ChangeSeriesPaths')}
        iconName={icons.FOLDER}
        onPress={onOpen}
      />

      <Modal
        isOpen={isOpen}
        closeOnBackgroundClick={false}
        onModalClose={onClose}
      >
        <ChangeSeriesPathsModalContent onModalClose={onClose} />
      </Modal>
    </>
  );
}

export default ChangeSeriesPathsToolbarButton;
