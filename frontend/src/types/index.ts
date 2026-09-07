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
  workStartedAtUtc: string | null;
  workEndedAtUtc: string | null;
  openedAtUtc: string | null;
  closedAtUtc: string | null;
  resolutionSummary: string | null;
  technicalNotes: string | null;
  createdAt: string;
  attachments: TicketAttachmentDto[];
  messages: TicketMessageDto[];
}

export interface CreatedUserResponse {
  userId: string;
  email: string;
  temporaryPassword: string;
  temporaryPasswordExpiresAtUtc: string;
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
  slaComplianceRate: number;
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
  startedAtUtc: string | null;
  endedAtUtc: string | null;
  createdAt: string;
  history: WorkTaskAssignmentLogDto[];
}

export interface GanttEntryDto {
  type: 'Ticket' | 'WorkTask';
  id: string;
  title: string;
  subLabel: string | null;
  startedAtUtc: string | null;
  endedAtUtc: string | null;
  status: string;
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
