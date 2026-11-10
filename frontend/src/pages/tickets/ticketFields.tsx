import { Field, Input, Label, Select } from '../../components/ui';
import { useLanguage } from '../../i18n/LanguageContext';

/** A labelled choice from a list; `placeholder` adds an empty first entry that says nothing is chosen yet. */
export function SelectField({
  label,
  value,
  onChange,
  options,
  placeholder,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  options: { value: string; label: string }[];
  placeholder?: string;
}) {
  return (
    <Field>
      <Label>{label}</Label>
      <Select value={value} onChange={(e) => onChange(e.target.value)}>
        {placeholder !== undefined && <option value="">{placeholder}</option>}
        {options.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </Select>
    </Field>
  );
}

export function DueDateField({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  const { t } = useLanguage();
  return (
    <Field>
      <Label>{t('ticketFields_dueDate')}</Label>
      <Input type="date" value={value} onChange={(e) => onChange(e.target.value)} />
      <p className="mt-1 text-xs text-slate-600">{t('ticketFields_dueHint')}</p>
    </Field>
  );
}
