export type UserRole = 'Admin' | 'Employee' | 'Client';

export type TicketStatus = 'New' | 'Open' | 'InProgress' | 'Resolved' | 'Closed';

export type TicketSource = 'Phone' | 'Email' | 'TicketSystem' | 'Other';

export type MessageType = 'Response' | 'InternalNote' | 'SystemEvent';

export type TicketPriority = 'Emergency' | 'High' | 'Medium' | 'Low';

export interface UserSummary {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  role: UserRole;
  companyId: string | null;
  phoneNumber: string | null;
  phoneNumberPrompted: boolean;
  departmentId: string | null;
  departmentName: string | null;
  subBranchId: string | null;
  subBranchName: string | null;
}

export interface LoginResponse {
  requiresPasswordChange: boolean;
  accessToken: string | null;
  expiresAtUtc: string | null;
  user: UserSummary | null;
}

export interface LookupItem {
  id: string;
  name: string;
}

export interface SlaPlanItem extends LookupItem {
  resolutionTimeHours: number;
}

export interface LookupItemFull {
  id: string;
  name: string;
  isActive: boolean;
}

export interface DepartmentItemFull {
  id: string;
  name: string;
  email: string;
  isActive: boolean;
}

export interface CompanyItemFull {
  id: string;
  name: string;
  address: string | null;
  contactInfo: string | null;
  isActive: boolean;
  /** False: the text of this company's tickets is never sent to an outside AI provider (no knowledge-base chunk is kept). Absent = true. */
  aiIndexingEnabled?: boolean;
}

export interface SubBranchItemFull {
  id: string;
  name: string;
  isActive: boolean;
  departmentId: string;
}

export interface SlaPlanItemFull {
  id: string;
  name: string;
  resolutionTimeHours: number;
  isActive: boolean;
}

/** A client of a company as the pickers of the ticket form offer them: the name, and the address that tells two people of the same name apart. */
export interface CompanyClientItem {
  id: string;
  name: string;
  email: string;
}

export interface ClientLookupResult {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  phoneNumber: string | null;
  companyId: string | null;
  companyName: string | null;
}

export interface KnowledgeBaseSearchResultDto {
  sourceType: 'Ticket' | 'Document';
  ticketId: string | null;
  ticketNumber: string | null;
  status: TicketStatus | null;
  closedAtUtc: string | null;
  resolutionSummary: string | null;
  documentId: string | null;
  documentFileUrl: string | null;
  title: string;
  matchScore: number;
}

export interface SimilarTicketsResponseDto {
  similarTicketCount: number;
  totalEligibleTicketCount: number;
  similarPercentage: number;
  tickets: KnowledgeBaseSearchResultDto[];
}

export interface KnowledgeBaseDocumentDto {
  id: string;
  title: string;
  sourceFileName: string;
  fileUrl: string | null;
  createdAt: string;
  chunkCount: number;
}

export interface TicketAttachmentDto {
  id: string;
  fileName: string;
  fileUrl: string;
  fileSizeBytes: number;
  contentType: string;
  createdAt: string;
}

export interface TicketMessageDto {
  id: string;
  type: MessageType;
  bodyHtml: string;
  authorId: string;
  authorName: string;
  createdAt: string;
  attachments: TicketAttachmentDto[];
}

export interface TicketListItem {
  id: string;
  ticketNumber: string;
  title: string;
  status: TicketStatus;
  priority: TicketPriority | null;
  companyName: string;
  clientId: string;
  clientName: string;
  assignedToName: string | null;
  createdAt: string;
  dueDateUtc: string | null;
  closedAtUtc: string | null;
  departmentId: string | null;
  departmentName: string | null;
  subBranchId: string | null;
  subBranchName: string | null;
}

export interface TicketAssigneeDto {
  userId: string;
  name: string;
}

/** One stretch of work on a ticket, by the person who did it. endedAtUtc is null while it is still running. */
export interface TicketWorkSessionDto {
  id: string;
  userId: string;
  userName: string;
  startedAtUtc: string;
  endedAtUtc: string | null;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface TicketCountsDto {
  total: number;
  new: number;
  opened: number;
  closed: number;
  mine: number;
}

export interface TicketDetailDto {
  id: string;
  ticketNumber: string;
  title: string;
  description: string;
  status: TicketStatus;
  clientId: string;
  clientName: string;
  clientEmail: string | null;
  clientPhoneNumber: string | null;
  companyId: string;
  companyName: string;
  source: TicketSource | null;
  helpTopicId: string | null;
  helpTopicName: string | null;
  departmentId: string | null;
  departmentName: string | null;
  subBranchId: string | null;
  subBranchName: string | null;
  slaPlanId: string | null;
  slaPlanName: string | null;
  priority: TicketPriority | null;
  dueDateUtc: string | null;
  categoryId: string | null;
  categoryName: string | null;
  assignedToUserId: string | null;
  assignedToName: string | null;
  assignees: TicketAssigneeDto[];
  /** Staff only: the client always gets an empty list. */
  workSessions: TicketWorkSessionDto[];
  openedAtUtc: string | null;
  closedAtUtc: string | null;
  resolutionSummary: string | null;
  createdAt: string;
  attachments: TicketAttachmentDto[];
  messages: TicketMessageDto[];
}

export interface CreatedUserResponse {
  userId: string;
  email: string;
  /** Null when the caller is not allowed to see it (it was only emailed to the account owner). */
  temporaryPassword: string | null;
  temporaryPasswordExpiresAtUtc: string;
}

export interface UserPageDto {
  items: UserListItemDto[];
  page: number;
  pageSize: number;
  totalCount: number;
  /** Totals ignore the search text, so the tab labels keep showing how many accounts exist. */
  clientCount: number;
  staffCount: number;
}

export interface UserListItemDto {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  role: UserRole;
  companyId: string | null;
  companyName: string | null;
  phoneNumber: string | null;
  isActive: boolean;
  emailConfirmed: boolean;
  createdAtUtc: string;
  departmentId: string | null;
  departmentName: string | null;
  subBranchId: string | null;
  subBranchName: string | null;
  profilePictureUrl: string | null;
  lastLoginAtUtc: string | null;
}

export interface ReportSummaryDto {
  totalNew: number;
  totalOpen: number;
  totalClosed: number;
  avgResolutionHours: number;
  slaComplianceRate: number | null;
}

export interface TimeSeriesPointDto {
  date: string;
  opened: number;
  closed: number;
}

export interface LeaderboardEntryDto {
  userId: string;
  name: string;
  closedCount: number;
}

export interface BranchBreakdownEntryDto {
  departmentId: string;
  departmentName: string;
  totalNew: number;
  totalOpen: number;
  totalClosed: number;
}

export interface PeriodComparisonPointDto {
  label: string;
  periodStart: string;
  count: number;
}

export interface TopIssueDto {
  helpTopicName: string;
  count: number;
}

export interface OverdueNotificationSettingsDto {
  notifyOnSlaBreach: boolean;
  manualOverdueDays: number | null;
}

export type WorkTaskStatus = 'Pending' | 'InProgress' | 'Done';

export interface WorkTaskAssignmentLogDto {
  assignedByUserId: string;
  assignedByName: string;
  assignedToUserId: string;
  assignedToName: string;
  assignedAtUtc: string;
}

export interface WorkTaskListItem {
  id: string;
  title: string;
  status: WorkTaskStatus;
  departmentId: string;
  departmentName: string;
  createdByUserId: string;
  createdByName: string;
  assignedByUserId: string;
  assignedByName: string;
  assignedToUserId: string;
  assignedToName: string;
  startedAtUtc: string | null;
  endedAtUtc: string | null;
  createdAt: string;
}

export interface WorkTaskDetailDto {
  id: string;
  title: string;
  description: string;
  status: WorkTaskStatus;
  departmentId: string;
  departmentName: string;
  createdByUserId: string;
  createdByName: string;
  assignedByUserId: string;
  assignedByName: string;
  assignedToUserId: string;
  assignedToName: string;
  /** The first start and the last end of the task's work sessions (no end while one is running). */
  startedAtUtc: string | null;
  endedAtUtc: string | null;
  createdAt: string;
  history: WorkTaskAssignmentLogDto[];
  /** Every stretch of work on the task, oldest first. */
  sessions: WorkTaskSessionDto[];
}

export interface WorkTaskSessionDto {
  id: string;
  userId: string;
  userName: string;
  startedAtUtc: string;
  endedAtUtc: string | null;
}

export interface GanttEntryDto {
  type: 'Ticket' | 'WorkTask';
  id: string;
  title: string;
  subLabel: string | null;
  startedAtUtc: string | null;
  endedAtUtc: string | null;
  status: string;
  /** False for the stand-in block of a ticket nobody logged work on: it shows how long the ticket was open, not effort. */
  isLogged: boolean;
}

export interface GanttResponse {
  userId: string;
  userName: string;
  date: string;
  entries: GanttEntryDto[];
}

export interface BranchGanttResponse {
  departmentId: string;
  departmentName: string;
  date: string;
  users: GanttResponse[];
}

// ---- the statistics of the companies (second tab of the dashboard)

export interface CompanyRowDto {
  companyId: string;
  name: string;
  total: number;
  new: number;
  open: number;
  closed: number;
  avgResolutionHours: number;
  slaComplianceRate: number | null;
  previousTotal: number;
  sharePercent: number;
}

export interface TopCompaniesDto {
  items: CompanyRowDto[];
  totalTickets: number;
  topTickets: number;
  totalCompanies: number;
}

export interface CompanyListPageDto {
  items: CompanyRowDto[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalTickets: number;
}

export interface CompanySummaryDto {
  total: number;
  new: number;
  open: number;
  closed: number;
  avgResolutionHours: number;
  slaComplianceRate: number | null;
  previousTotal: number;
}

export interface CompanyClientStatDto {
  clientId: string;
  name: string;
  total: number;
  open: number;
  closed: number;
}

/** `id` null is "none": a ticket that has not been given a topic / category yet. */
export interface BreakdownItemDto {
  id: string | null;
  name: string | null;
  count: number;
  isActive: boolean;
}

export interface SubBranchCountDto {
  subBranchId: string;
  name: string;
  count: number;
}

export interface BranchCountDto {
  departmentId: string | null;
  name: string | null;
  count: number;
  noSubBranchCount: number;
  subBranches: SubBranchCountDto[];
}

export interface CloserBranchDto {
  departmentId: string | null;
  name: string | null;
  count: number;
}

export interface CloserStatDto {
  userId: string;
  name: string;
  closedCount: number;
  branches: CloserBranchDto[];
}

export interface ClosedByDto {
  people: CloserStatDto[];
  branches: BranchCountDto[];
  totalClosed: number;
}

// ---- the extra statistics of the companies

export interface OpenAgeDto {
  total: number;
  upTo2Days: number;
  upTo7Days: number;
  upTo30Days: number;
  older: number;
  oldestDays: number;
  oldestCompany: string | null;
}

export interface TimeSpentDto {
  totalHours: number;
  sessions: number;
  tickets: number;
  avgHoursPerTicket: number;
  runningSessions: number;
}

export interface ConversationDto {
  tickets: number;
  avgMessagesPerTicket: number;
  pickedUp: number;
  avgHoursToPickup: number;
  notPickedUp: number;
}

export interface RhythmDto {
  total: number;
  byWeekday: number[];
  byHour: number[];
  timeZone: string;
}

export interface RepeatedTopicDto {
  clientId: string;
  clientName: string;
  topicId: string;
  topicName: string;
  count: number;
}

export interface RepeatedTopicsDto {
  groups: number;
  tickets: number;
  items: RepeatedTopicDto[];
}

export interface QuietCompanyDto {
  companyId: string;
  name: string;
  previousTotal: number;
  lastTicketAtUtc: string | null;
}

export interface QuietCompaniesDto {
  total: number;
  items: QuietCompanyDto[];
}

// The reports of the "Tickets" tab (api/reports/tickets).
export interface TicketBriefDto {
  id: string;
  ticketNumber: string;
  title: string;
  status: TicketStatus;
  createdAt: string;
  dueDateUtc: string | null;
  lastActivityUtc: string | null;
  ageDays: number;
}

export interface TicketBriefListDto {
  total: number;
  items: TicketBriefDto[];
}

export type AgeGroupKey = 'upTo2Days' | 'upTo7Days' | 'upTo30Days' | 'older';

export interface AgeGroupDto {
  key: string;
  count: number;
  oldest: TicketBriefDto | null;
}

export interface AttentionDto {
  asOfUtc: string;
  activeTotal: number;
  ages: AgeGroupDto[];
  inactiveDays: number;
  inactive: TicketBriefListDto;
  unassigned: TicketBriefListDto;
  dueSoon: TicketBriefListDto;
  overdue: TicketBriefListDto;
}

export interface ResolutionTimeDto {
  count: number;
  averageHours: number;
  medianHours: number;
  p90Hours: number;
}

export interface FirstResponseDto {
  tickets: number;
  answered: number;
  unanswered: number;
  averageHours: number;
  medianHours: number;
  p90Hours: number;
}

export interface TransferEdgeDto {
  fromId: string | null;
  fromName: string | null;
  toId: string;
  toName: string;
  count: number;
}

export interface TransferItemDto {
  ticketId: string;
  ticketNumber: string;
  title: string;
  atUtc: string;
  fromId: string | null;
  fromName: string | null;
  toId: string;
  toName: string;
}

export interface TransfersDto {
  total: number;
  edges: TransferEdgeDto[];
  items: TransferItemDto[];
}

// The reports about people (api/reports/employees). A day is a date ("2026-03-10") in the business time zone.
export interface PersonLoadDto {
  userId: string;
  name: string;
  branch: string | null;
  openTickets: number;
  mainTickets: number;
  overdueTickets: number;
  openTasks: number;
}

export interface WorkloadDto {
  asOfUtc: string;
  people: PersonLoadDto[];
}

export interface PersonHoursDto {
  userId: string;
  name: string;
  branch: string | null;
  ticketHours: number;
  taskHours: number;
  totalHours: number;
  daysWithoutEntry: number | null;
}

export interface HoursPeopleDto {
  people: PersonHoursDto[];
}

export interface DayHoursDto {
  day: string;
  ticketHours: number;
  taskHours: number;
  totalHours: number;
}

export interface WeekHoursDto {
  weekStart: string;
  ticketHours: number;
  taskHours: number;
  totalHours: number;
}

export interface HoursDetailDto {
  userId: string;
  name: string;
  ticketHours: number;
  taskHours: number;
  totalHours: number;
  days: DayHoursDto[];
  weeks: WeekHoursDto[];
  daysWithoutEntry: string[];
  daysWithoutEntryApply: boolean;
}

export interface SpecializationItemDto {
  id: string;
  name: string;
  closed: number;
  hours: number;
}

export interface SpecializationDto {
  userId: string;
  name: string;
  closedTotal: number;
  topics: SpecializationItemDto[];
  categories: SpecializationItemDto[];
  companies: SpecializationItemDto[];
}

export interface CompanyDependencyDto {
  companyId: string;
  company: string;
  closed: number;
  personId: string;
  person: string;
  personClosed: number;
  sharePercent: number;
  high: boolean;
}

export interface DependencyDto {
  total: number;
  minClosed: number;
  minSharePercent: number;
  items: CompanyDependencyDto[];
}
