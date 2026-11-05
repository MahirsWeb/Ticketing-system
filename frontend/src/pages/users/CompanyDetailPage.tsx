import { useParams } from 'react-router-dom';
import { useCompany } from '../../api/adminLookupQueries';
import { ErrorText, Spinner } from '../../components/ui';
import { messageOf } from '../../lib/messageOf';
import { CompanyView } from './CompanyView';

function LoadedCompany({ id }: { id: string }) {
  const company = useCompany(id);
  if (company.isPending) {
    return (
      <div role="status" aria-label="Loading the company" className="flex justify-center py-16">
        <Spinner className="h-8 w-8 text-blue-700" />
      </div>
    );
  }
  if (company.isError) return <ErrorText>{messageOf(company.error, 'Could not load this company.')}</ErrorText>;
  return <CompanyView company={company.data} />;
}

/** The page of one company. Another company is a page of its own: what was searched for, chosen or being edited in the one before is not carried over. */
export default function CompanyDetailPage() {
  const { id } = useParams<{ id: string }>();
  return id ? <LoadedCompany key={id} id={id} /> : null;
}
