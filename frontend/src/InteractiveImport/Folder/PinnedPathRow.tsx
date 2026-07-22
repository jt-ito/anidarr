import React, { useCallback } from 'react';
import IconButton from 'Components/Link/IconButton';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableRow from 'Components/Table/TableRow';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

interface PinnedPathRowProps {
  id: string;
  label: string;
  folder: string;
  isActive: boolean;
  onPress(folder: string): void;
  onSetActive(id: string): void;
  onRemove(id: string): void;
}

function PinnedPathRow(props: PinnedPathRowProps) {
  const { id, label, folder, isActive, onPress, onSetActive, onRemove } = props;

  const handlePress = useCallback(() => {
    onPress(folder);
  }, [folder, onPress]);

  const handleSetActivePress = useCallback(
    (e: React.MouseEvent) => {
      e.stopPropagation();
      onSetActive(id);
    },
    [id, onSetActive]
  );

  const handleRemovePress = useCallback(
    (e: React.MouseEvent) => {
      e.stopPropagation();
      onRemove(id);
    },
    [id, onRemove]
  );

  return (
    <TableRow onClick={handlePress}>
      <TableRowCell>{label}</TableRowCell>
      <TableRowCell>{folder}</TableRowCell>
      <TableRowCell>
        <IconButton
          name={isActive ? icons.HEART : icons.HEART_OUTLINE}
          title={
            isActive ? translate('ActivePinnedPath') : translate('SetActive')
          }
          kind={isActive ? kinds.PRIMARY : kinds.DEFAULT}
          onPress={handleSetActivePress}
        />
        <IconButton
          name={icons.DELETE}
          title={translate('Delete')}
          kind={kinds.DANGER}
          onPress={handleRemovePress}
        />
      </TableRowCell>
    </TableRow>
  );
}

export default PinnedPathRow;
