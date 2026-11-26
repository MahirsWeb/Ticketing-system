import { useEffect, useMemo, useRef, useState } from 'react';
import { useCompanies } from '../../api/lookupQueries';
import { useClientByEmail, useCompanyClientChoices } from '../../api/userQueries';
import { Card, ErrorText, Field, Label } from '../../components/ui';
import { Combobox } from '../../components/Combobox';
import { useLanguage } from '../../i18n/LanguageContext';

/** What the two boxes hold: the company, and a person of that company. The person is never chosen without the company, and goes when the company does. */
export interface ClientPick {
  companyId: string | null;
  clientId: string | null;
}

/**
 * The first step of a ticket opened for a client: a company, then a person of it. Both are searched by typing and only an existing one can be chosen. The
 * person cannot be chosen before the company; the people offered are those of the company chosen. When the page was opened from an e-mail the sender is
 * looked for at once (`autoLookupEmail`) and, if there is an account for them in a company that is on the list, both boxes start with them; otherwise the
 * person is told to choose by hand. What the person chooses by hand wins: once they have touched a box, the answer of the automatic search is no longer heard.
 */
export function ClientPicker({ value, onChange, autoLookupEmail }: { value: ClientPick; onChange: (next: ClientPick) => void; autoLookupEmail?: string }) {
  const { t } = useLanguage();
  const companies = useCompanies();
  const choices = useCompanyClientChoices(value.companyId);
  const automatic = useClientByEmail(autoLookupEmail);
  const [touched, setTouched] = useState(false);
  // Who is told is whoever the page names now; a page that names another one does not make the search ask again.
  const tell = useRef(onChange);
  useEffect(() => {
    tell.current = onChange;
  });

  const companyOptions = useMemo(() => (companies.data ?? []).map((c) => ({ id: c.id, label: c.name })), [companies.data]);
  const clientOptions = useMemo(() => (choices.data ?? []).map((c) => ({ id: c.id, label: c.name, secondary: c.email })), [choices.data]);

  const sender = automatic.data;
  const senderUsable = !!sender?.companyId && companyOptions.some((c) => c.id === sender.companyId);
  useEffect(() => {
    if (touched || !sender || !senderUsable) return;
    tell.current({ companyId: sender.companyId, clientId: sender.id });
  }, [touched, sender, senderUsable]);

  // The sender of the e-mail has no account here, or an account in a company that cannot be chosen: said once the lists are there to say it from.
  const senderMissing = !!autoLookupEmail && !touched && (automatic.isError || (!!sender && !!companies.data && !senderUsable));
  const chosen = choices.data?.find((c) => c.id === value.clientId);

  return (
    <Card className="p-5">
      <h2 className="mb-3 text-sm font-bold uppercase tracking-wide text-slate-600">{t('picker_title')}</h2>
      <div className="grid gap-4 sm:grid-cols-2">
        <Field>
          <Label>{t('picker_company')}</Label>
          <Combobox
            options={companyOptions}
            value={value.companyId}
            busy={companies.isPending}
            placeholder={t('picker_companyPlaceholder')}
            noResultsText={t('picker_noCompanies')}
            onChange={(id) => {
              setTouched(true);
              onChange({ companyId: id, clientId: null });
            }}
          />
          {companies.isError && <ErrorText>{t('picker_companiesFail')}</ErrorText>}
        </Field>
        <Field>
          <Label>{t('picker_client')}</Label>
          <Combobox
            options={clientOptions}
            value={value.clientId}
            disabled={!value.companyId}
            busy={choices.isFetching}
            placeholder={value.companyId ? t('picker_clientPlaceholder') : t('picker_clientFirst')}
            noResultsText={t('picker_noClients')}
            onChange={(id) => {
              setTouched(true);
              onChange({ companyId: value.companyId, clientId: id });
            }}
          />
          {choices.isError && <ErrorText>{t('picker_clientsFail')}</ErrorText>}
        </Field>
      </div>
      {chosen && <p className="mt-2 text-sm text-slate-600">{chosen.email}</p>}
      <ErrorText>{senderMissing ? t('picker_senderNotFound') : null}</ErrorText>
    </Card>
  );
}
