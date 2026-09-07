import { useRef } from 'react';
import clsx from 'clsx';

const SIZES: Record<'sm' | 'lg', string> = {
  sm: 'h-10 w-10',
  lg: 'h-20 w-20',
};

export function UserAvatar({
  url,
  size = 'lg',
  editable = false,
  uploading = false,
  onUpload,
  className,
}: {
  url: string | null;
  size?: 'sm' | 'lg';
  editable?: boolean;
  uploading?: boolean;
  onUpload?: (file: File) => void;
  className?: string;
}) {
  const inputRef = useRef<HTMLInputElement>(null);

  return (
    <div className={clsx('group relative shrink-0 overflow-hidden rounded-full bg-slate-100', SIZES[size], className)}>
      {url ? (
        <img src={url} alt="" className="h-full w-full object-cover" />
      ) : (
        <svg viewBox="0 0 24 24" fill="currentColor" className="h-full w-full p-2 text-slate-300">
          <path d="M12 12c2.7 0 4.9-2.2 4.9-4.9S14.7 2.2 12 2.2 7.1 4.4 7.1 7.1 9.3 12 12 12zm0 2.4c-3.3 0-9.8 1.6-9.8 4.9v1.5c0 .8.6 1.4 1.4 1.4h16.8c.8 0 1.4-.6 1.4-1.4v-1.5c0-3.3-6.5-4.9-9.8-4.9z" />
        </svg>
      )}

      {editable && (
        <>
          <button
            type="button"
            disabled={uploading}
            onClick={() => inputRef.current?.click()}
            className="absolute inset-0 flex items-center justify-center bg-black/0 text-[10px] font-medium text-transparent transition group-hover:bg-black/40 group-hover:text-white disabled:cursor-wait"
          >
            {uploading ? '…' : 'Change'}
          </button>
          <input
            ref={inputRef}
            type="file"
            accept="image/png,image/jpeg,image/gif,image/webp"
            className="hidden"
            onChange={(e) => {
              const file = e.target.files?.[0];
              if (file && onUpload) onUpload(file);
              e.target.value = '';
            }}
          />
        </>
      )}
    </div>
  );
}
