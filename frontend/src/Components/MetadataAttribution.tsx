import React from 'react';
import Link from 'Components/Link/Link';
import translate from 'Utilities/String/translate';
import styles from './MetadataAttribution.css';

interface MetadataAttributionProps {
  providerName?: string;
}

export default function MetadataAttribution({
  providerName = 'TheTVDB',
}: MetadataAttributionProps) {
  return (
    <div className={styles.container}>
      <Link className={styles.attribution} to="/settings/metadatasource">
        {translate('MetadataProvidedBy', { provider: providerName })}
      </Link>
    </div>
  );
}
