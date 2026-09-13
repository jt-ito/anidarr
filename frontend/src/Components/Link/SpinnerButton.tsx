import classNames from 'classnames';
import React from 'react';
import Icon, { IconName } from 'Components/Icon';
import { icons } from 'Helpers/Props';
import Button, { ButtonProps } from './Button';
import styles from './SpinnerButton.css';

export interface SpinnerButtonProps extends ButtonProps {
  isSpinning: boolean;
  isDisabled?: boolean;
  spinnerIcon?: IconName;
  spinningLabel?: React.ReactNode;
}

function SpinnerButton({
  className = styles.button,
  isSpinning,
  isDisabled,
  spinnerIcon = icons.SPINNER,
  spinningLabel,
  children,
  ...otherProps
}: SpinnerButtonProps) {
  const hasSpinningLabel = isSpinning && Boolean(spinningLabel);

  return (
    <Button
      className={classNames(
        className,
        styles.button,
        isSpinning && styles.isSpinning,
        hasSpinningLabel && styles.hasSpinningLabel
      )}
      isDisabled={isDisabled || isSpinning}
      {...otherProps}
    >
      <span className={styles.spinnerContainer}>
        {hasSpinningLabel && (
          <span
            className={styles.spinningLabel}
            title={
              typeof spinningLabel === 'string' ? spinningLabel : undefined
            }
          >
            {spinningLabel}
          </span>
        )}
        <Icon className={styles.spinner} name={spinnerIcon} isSpinning={true} />
      </span>

      <span className={styles.label}>{children}</span>
    </Button>
  );
}

export default SpinnerButton;
