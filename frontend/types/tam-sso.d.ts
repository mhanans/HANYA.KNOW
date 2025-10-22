import type { DetailedHTMLProps, HTMLAttributes } from 'react';

declare global {
  namespace JSX {
    interface IntrinsicElements {
      'tam-sso': DetailedHTMLProps<HTMLAttributes<HTMLElement>, HTMLElement> & {
        app?: string;
        server?: string;
        scope?: string;
        'redirect-uri'?: string;
        'auto-submit'?: string;
      };
    }
  }
}

export {};
