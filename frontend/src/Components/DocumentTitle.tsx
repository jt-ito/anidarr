import React, { useEffect } from 'react';

interface DocumentTitleProps {
  title?: string;
  children?: React.ReactNode;
}

export default function DocumentTitle({ title, children }: DocumentTitleProps) {
  useEffect(() => {
    if (title) {
      document.title = title;
    }
  }, [title]);

  return <>{children}</>;
}
