/**
 * T-41: the hand-written types in `index.ts` are checked here against what the server really sends, which `npm run gen:api` generates
 * from `docs/openapi.json` (the backend's `ApiContractTests` keeps that file true). Nothing of this exists at run time; it is
 * the build (`tsc`) that fails when a response changes on the server and the type next to it does not:
 *   - a field the screen reads is no longer sent, or has another name,
 *   - a field the server can send as `null` is typed as always present,
 *   - a text the screen expects as a number (or the other way round).
 * What it does not catch: a field the server added and the screen does not know about (harmless), and the requests the screen sends.
 *
 * When it fails: change the hand-written type to what the server sends (or, if the server was changed by mistake, the server).
 */
import type { components } from './api.generated';
import type * as T from './index';

type S = components['schemas'];

type Expect<X extends true> = X;

/** The server writes every property of a response, a missing value as `null`, so "optional" in the description is not "may be absent". */
type Full<X> = X extends (infer U)[] ? Full<U>[] : X extends object ? { [K in keyof X]-?: Full<X[K]> } : X;

/** The server sends a status as plain text; the screens narrow it to the statuses they know. */
type Widen<X> = X extends string ? string : X extends (infer U)[] ? Widen<U>[] : X extends object ? { [K in keyof X]: Widen<X[K]> } : X;

/** Everything the server sends can be used as the hand-written type. */
type Conforms<Server, Client> = [Full<Server>] extends [Widen<Client>] ? true : false;

/** Two lists of names are the same list. */
type SameNames<A, B> = [A] extends [B] ? ([B] extends [A] ? true : false) : false;

export type ContractChecks = [
  // lists of allowed values
  Expect<SameNames<S['UserRole'], T.UserRole>>,
  Expect<SameNames<S['TicketStatus'], T.TicketStatus>>,
  Expect<SameNames<S['TicketSource'], T.TicketSource>>,
  Expect<SameNames<S['MessageType'], T.MessageType>>,
  Expect<SameNames<S['TicketPriority'], T.TicketPriority>>,
  Expect<SameNames<S['WorkTaskStatus'], T.WorkTaskStatus>>,

  // people, sign-in
  Expect<Conforms<S['UserSummary'], T.UserSummary>>,
  Expect<Conforms<S['LoginResponse'], T.LoginResponse>>,
  Expect<Conforms<S['CreatedUserResponse'], T.CreatedUserResponse>>,
  Expect<Conforms<S['UserListItem'], T.UserListItemDto>>,
  Expect<Conforms<S['UserPageDto'], T.UserPageDto>>,
  Expect<Conforms<S['ClientLookupResult'], T.ClientLookupResult>>,

  // lookups
  Expect<Conforms<S['LookupItem'], T.LookupItem>>,
  Expect<Conforms<S['LookupItemFull'], T.LookupItemFull>>,
  Expect<Conforms<S['DepartmentItemFull'], T.DepartmentItemFull>>,
  Expect<Conforms<S['CompanyItemFull'], T.CompanyItemFull>>,
  Expect<Conforms<S['SubBranchItemFull'], T.SubBranchItemFull>>,
  Expect<Conforms<S['SlaPlanItemFull'], T.SlaPlanItemFull>>,

  // tickets
  Expect<Conforms<S['TicketListItem'], T.TicketListItem>>,
  Expect<Conforms<S['TicketListItemPagedResult'], T.PagedResult<T.TicketListItem>>>,
  Expect<Conforms<S['TicketCountsDto'], T.TicketCountsDto>>,
  Expect<Conforms<S['TicketDetailDto'], T.TicketDetailDto>>,
  Expect<Conforms<S['TicketMessageDto'], T.TicketMessageDto>>,
  Expect<Conforms<S['TicketAttachmentDto'], T.TicketAttachmentDto>>,
  Expect<Conforms<S['TicketAssigneeDto'], T.TicketAssigneeDto>>,
  Expect<Conforms<S['TicketWorkSessionDto'], T.TicketWorkSessionDto>>,

  // work tasks and the day chart
  Expect<Conforms<S['WorkTaskListItem'], T.WorkTaskListItem>>,
  Expect<Conforms<S['WorkTaskDetailDto'], T.WorkTaskDetailDto>>,
  Expect<Conforms<S['WorkTaskAssignmentLogDto'], T.WorkTaskAssignmentLogDto>>,
  Expect<Conforms<S['WorkTaskSessionDto'], T.WorkTaskSessionDto>>,
  Expect<Conforms<S['GanttEntryDto'], T.GanttEntryDto>>,
  Expect<Conforms<S['GanttResponse'], T.GanttResponse>>,
  Expect<Conforms<S['BranchGanttResponse'], T.BranchGanttResponse>>,

  // reports
  Expect<Conforms<S['ReportSummaryDto'], T.ReportSummaryDto>>,
  Expect<Conforms<S['TimeSeriesPointDto'], T.TimeSeriesPointDto>>,
  Expect<Conforms<S['LeaderboardEntryDto'], T.LeaderboardEntryDto>>,
  Expect<Conforms<S['BranchBreakdownEntryDto'], T.BranchBreakdownEntryDto>>,
  Expect<Conforms<S['PeriodComparisonPointDto'], T.PeriodComparisonPointDto>>,
  Expect<Conforms<S['TopIssueDto'], T.TopIssueDto>>,
  Expect<Conforms<S['CompanyRowDto'], T.CompanyRowDto>>,
  Expect<Conforms<S['TopCompaniesDto'], T.TopCompaniesDto>>,
  Expect<Conforms<S['CompanyListPageDto'], T.CompanyListPageDto>>,
  Expect<Conforms<S['CompanySummaryDto'], T.CompanySummaryDto>>,
  Expect<Conforms<S['CompanyClientStatDto'], T.CompanyClientStatDto>>,
  Expect<Conforms<S['BreakdownItemDto'], T.BreakdownItemDto>>,
  Expect<Conforms<S['BranchCountDto'], T.BranchCountDto>>,
  Expect<Conforms<S['ClosedByDto'], T.ClosedByDto>>,
  Expect<Conforms<S['OpenAgeDto'], T.OpenAgeDto>>,
  Expect<Conforms<S['TimeSpentDto'], T.TimeSpentDto>>,
  Expect<Conforms<S['ConversationDto'], T.ConversationDto>>,
  Expect<Conforms<S['RhythmDto'], T.RhythmDto>>,
  Expect<Conforms<S['RepeatedTopicsDto'], T.RepeatedTopicsDto>>,
  Expect<Conforms<S['QuietCompaniesDto'], T.QuietCompaniesDto>>,
  Expect<Conforms<S['TicketBriefDto'], T.TicketBriefDto>>,
  Expect<Conforms<S['TicketBriefListDto'], T.TicketBriefListDto>>,
  Expect<Conforms<S['AgeGroupDto'], T.AgeGroupDto>>,
  Expect<Conforms<S['AttentionDto'], T.AttentionDto>>,
  Expect<Conforms<S['ResolutionTimeDto'], T.ResolutionTimeDto>>,
  Expect<Conforms<S['FirstResponseDto'], T.FirstResponseDto>>,
  Expect<Conforms<S['TransferEdgeDto'], T.TransferEdgeDto>>,
  Expect<Conforms<S['TransferItemDto'], T.TransferItemDto>>,
  Expect<Conforms<S['TransfersDto'], T.TransfersDto>>,
  Expect<Conforms<S['PersonLoadDto'], T.PersonLoadDto>>,
  Expect<Conforms<S['WorkloadDto'], T.WorkloadDto>>,
  Expect<Conforms<S['PersonHoursDto'], T.PersonHoursDto>>,
  Expect<Conforms<S['HoursPeopleDto'], T.HoursPeopleDto>>,
  Expect<Conforms<S['DayHoursDto'], T.DayHoursDto>>,
  Expect<Conforms<S['WeekHoursDto'], T.WeekHoursDto>>,
  Expect<Conforms<S['HoursDetailDto'], T.HoursDetailDto>>,
  Expect<Conforms<S['SpecializationItemDto'], T.SpecializationItemDto>>,
  Expect<Conforms<S['SpecializationDto'], T.SpecializationDto>>,
  Expect<Conforms<S['CompanyDependencyDto'], T.CompanyDependencyDto>>,
  Expect<Conforms<S['DependencyDto'], T.DependencyDto>>,
  Expect<Conforms<S['TicketCardEventDto'], T.TicketCardEventDto>>,
  Expect<Conforms<S['StatusSpanDto'], T.StatusSpanDto>>,
  Expect<Conforms<S['StatusTotalDto'], T.StatusTotalDto>>,
  Expect<Conforms<S['TicketCardHeaderDto'], T.TicketCardHeaderDto>>,
  Expect<Conforms<S['TicketCardDto'], T.TicketCardDto>>,
  Expect<Conforms<S['CompanyMonthlyHeaderDto'], T.CompanyMonthlyHeaderDto>>,
  Expect<Conforms<S['StatusCountDto'], T.StatusCountDto>>,
  Expect<Conforms<S['NamedCountDto'], T.NamedCountDto>>,
  Expect<Conforms<S['CompanyMonthlyTicketDto'], T.CompanyMonthlyTicketDto>>,
  Expect<Conforms<S['CompanyMonthlyDto'], T.CompanyMonthlyDto>>,
  Expect<Conforms<S['YearCountDto'], T.YearCountDto>>,
  Expect<Conforms<S['CompanyCardDto'], T.CompanyCardDto>>,
  Expect<Conforms<S['SubBranchLoadDto'], T.SubBranchLoadDto>>,
  Expect<Conforms<S['BranchLoadItemDto'], T.BranchLoadItemDto>>,
  Expect<Conforms<S['BranchLoadDto'], T.BranchLoadDto>>,

  // knowledge base, settings
  Expect<Conforms<S['KnowledgeBaseSearchResultDto'], T.KnowledgeBaseSearchResultDto>>,
  Expect<Conforms<S['SimilarTicketsResponseDto'], T.SimilarTicketsResponseDto>>,
  Expect<Conforms<S['KnowledgeBaseDocumentDto'], T.KnowledgeBaseDocumentDto>>,
  Expect<Conforms<S['OverdueNotificationSettingsDto'], T.OverdueNotificationSettingsDto>>,
];
