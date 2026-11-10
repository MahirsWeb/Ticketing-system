import { useEditor, EditorContent, type Editor } from '@tiptap/react';
import StarterKit from '@tiptap/starter-kit';
import Underline from '@tiptap/extension-underline';
import TextAlign from '@tiptap/extension-text-align';
import { TextStyle } from '@tiptap/extension-text-style';
import Link from '@tiptap/extension-link';
import { FontSize } from './FontSizeExtension';
import clsx from 'clsx';
import { useEffect, useRef } from 'react';

interface Props {
  value: string;
  onChange: (html: string) => void;
  placeholder?: string;
  /** Called with any files pasted (e.g. a screenshot) or picked via the toolbar's attach button. */
  onFilesSelected?: (files: File[]) => void;
  /** The editor is a contenteditable region, not a form control, so a <label for> cannot name it: it carries its own name. */
  ariaLabel?: string;
}

function ToolbarButton({
  active,
  onClick,
  children,
  title,
}: {
  active?: boolean;
  onClick: () => void;
  children: React.ReactNode;
  title: string;
}) {
  return (
    <button
      type="button"
      title={title}
      onMouseDown={(e) => e.preventDefault()}
      onClick={onClick}
      className={clsx(
        'rounded px-2 py-1 text-xs font-semibold hover:bg-slate-200',
        active ? 'bg-slate-300 text-slate-900' : 'text-slate-600'
      )}
    >
      {children}
    </button>
  );
}

function Toolbar({ editor, onAttachClick }: { editor: Editor; onAttachClick?: () => void }) {
  return (
    <div className="flex flex-wrap items-center gap-1 border-b border-slate-200 bg-slate-50 p-1.5">
      <ToolbarButton title="Bold" active={editor.isActive('bold')} onClick={() => editor.chain().focus().toggleBold().run()}>
        B
      </ToolbarButton>
      <ToolbarButton title="Italic" active={editor.isActive('italic')} onClick={() => editor.chain().focus().toggleItalic().run()}>
        <span className="italic">I</span>
      </ToolbarButton>
      <ToolbarButton title="Underline" active={editor.isActive('underline')} onClick={() => editor.chain().focus().toggleUnderline().run()}>
        <span className="underline">U</span>
      </ToolbarButton>
      <div className="mx-1 h-5 w-px bg-slate-300" />
      <select
        title="Font size"
        aria-label="Font size"
        className="rounded border border-slate-300 bg-white px-1 py-1 text-xs"
        onChange={(e) => (editor.chain().focus() as any).setFontSize(e.target.value).run()}
        defaultValue="16px"
      >
        {['12px', '14px', '16px', '18px', '20px', '24px', '28px'].map((s) => (
          <option key={s} value={s}>
            {s}
          </option>
        ))}
      </select>
      <div className="mx-1 h-5 w-px bg-slate-300" />
      <ToolbarButton title="Align left" active={editor.isActive({ textAlign: 'left' })} onClick={() => editor.chain().focus().setTextAlign('left').run()}>
        ⬅
      </ToolbarButton>
      <ToolbarButton title="Align center" active={editor.isActive({ textAlign: 'center' })} onClick={() => editor.chain().focus().setTextAlign('center').run()}>
        ↔
      </ToolbarButton>
      <ToolbarButton title="Align right" active={editor.isActive({ textAlign: 'right' })} onClick={() => editor.chain().focus().setTextAlign('right').run()}>
        ➡
      </ToolbarButton>
      <div className="mx-1 h-5 w-px bg-slate-300" />
      <ToolbarButton title="Bullet list" active={editor.isActive('bulletList')} onClick={() => editor.chain().focus().toggleBulletList().run()}>
        • List
      </ToolbarButton>
      <ToolbarButton title="Numbered list" active={editor.isActive('orderedList')} onClick={() => editor.chain().focus().toggleOrderedList().run()}>
        1. List
      </ToolbarButton>
      {onAttachClick && (
        <>
          <div className="mx-1 h-5 w-px bg-slate-300" />
          <ToolbarButton title="Attach a file" onClick={onAttachClick}>
            <span aria-hidden>📎</span> Attach
          </ToolbarButton>
        </>
      )}
    </div>
  );
}

export function RichTextEditor({ value, onChange, placeholder, onFilesSelected, ariaLabel }: Props) {
  const fileInputRef = useRef<HTMLInputElement>(null);

  const editor = useEditor({
    extensions: [
      StarterKit,
      Underline,
      TextStyle,
      FontSize,
      Link.configure({ openOnClick: false }),
      TextAlign.configure({ types: ['heading', 'paragraph'] }),
    ],
    content: value,
    onUpdate: ({ editor }) => onChange(editor.getHTML()),
    editorProps: {
      attributes: {
        class: 'tiptap prose prose-sm max-w-none px-3 py-2',
        'data-placeholder': placeholder ?? '',
        role: 'textbox',
        'aria-multiline': 'true',
        ...(ariaLabel ? { 'aria-label': ariaLabel } : {}),
      },
      handlePaste: (_view, event) => {
        if (!onFilesSelected) return false;
        const files = Array.from(event.clipboardData?.files ?? []);
        if (files.length === 0) return false;
        onFilesSelected(files);
        event.preventDefault();
        return true;
      },
    },
  });

  useEffect(() => {
    // Keep the visible editor in step with `value` whenever the parent changes it on its own — after a send (value becomes '')
    // and, critically, when the parent swaps in a different draft (e.g. switching Response <-> Internal note keeps a separate
    // draft per mode). Without this the contenteditable kept showing the previous mode's text while the state held the new
    // draft, so a reply could carry the internal note's words to the client. We only write when the incoming value really
    // differs from what the editor already shows, so ordinary typing (which flows value <- editor) never disturbs the cursor.
    // A destroyed editor has no schema any more and getHTML() throws on it: in development React runs every effect twice
    // (StrictMode) and the editor of the first run has already been torn down when the second one looks at it.
    if (!editor || editor.isDestroyed) return;
    const incoming = value === '' ? '<p></p>' : value;
    if (incoming !== editor.getHTML()) {
      editor.commands.setContent(value, { emitUpdate: false }); // do not emit an update, so this does not loop back through onChange
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the editor instance is stable; this must run when `value` changes
  }, [value]);

  if (!editor) return null;

  return (
    <div className="rounded-md border border-slate-300 bg-white focus-within:border-blue-600 focus-within:ring-2 focus-within:ring-blue-600">
      <Toolbar editor={editor} onAttachClick={onFilesSelected ? () => fileInputRef.current?.click() : undefined} />
      <EditorContent editor={editor} />
      {onFilesSelected && (
        <input
          ref={fileInputRef}
          type="file"
          aria-label="Attach files"
          multiple
          accept=".jpg,.jpeg,.png,.gif,.webp,.pdf,.doc,.docx,.xls,.xlsx,.txt"
          className="hidden"
          onChange={(e) => {
            const files = Array.from(e.target.files ?? []);
            if (files.length > 0) onFilesSelected(files);
            e.target.value = '';
          }}
        />
      )}
    </div>
  );
}
