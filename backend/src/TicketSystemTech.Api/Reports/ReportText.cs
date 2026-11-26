using TicketSystemTech.Api.Emails;

namespace TicketSystemTech.Api.Reports;

/// <summary>
/// The wording of the documents the server makes (the names of sheets and columns, the header): in the language the person who asked chose in the
/// application ("bs" or "en"), as the e-mails are. A key that is not here is shown as it is, so a missing word is seen and not hidden.
/// </summary>
public static class ReportText
{
    private static readonly Dictionary<string, (string En, string Bs)> Words = new()
    {
        ["ts_title"] = ("Monthly work sheet", "Mjesečni radni list"),
        ["ts_person"] = ("Person", "Osoba"),
        ["ts_month"] = ("Month", "Mjesec"),
        ["ts_madeBy"] = ("Made by", "Napravio/la"),
        ["ts_madeAt"] = ("Made at", "Napravljeno"),
        ["ts_snapshotLabel"] = ("Note", "Napomena"),
        ["ts_snapshot"] = (
            "The state at the moment it was made. Only recorded work sessions are counted (not the blocks the day chart draws for tickets with no recorded time); a session still running is counted up to that moment. Sessions can still be corrected later, and this document is not kept by the application.",
            "Stanje u trenutku pravljenja. Računaju se samo evidentirane radne sesije (ne blokovi koje dnevni grafikon crta za tikete bez evidentiranog vremena); sesija koja još traje računa se do tog trenutka. Sesije se naknadno mogu ispraviti, a aplikacija ovaj dokument ne čuva."),
        ["ts_sheetDays"] = ("Overview by days", "Pregled po danima"),
        ["ts_sheetCompanies"] = ("By company", "Po firmi"),
        ["ts_sheetItems"] = ("By ticket and task", "Po tiketu i zadatku"),
        ["ts_sheetRaw"] = ("Raw list", "Sirovi popis"),
        ["ts_day"] = ("Day", "Dan"),
        ["ts_ticketHours"] = ("Tickets (h)", "Tiketi (h)"),
        ["ts_taskHours"] = ("Tasks (h)", "Zadaci (h)"),
        ["ts_totalHours"] = ("Hours", "Sati"),
        ["ts_total"] = ("Total", "Ukupno"),
        ["ts_company"] = ("Company", "Firma"),
        ["ts_kind"] = ("Kind", "Vrsta"),
        ["ts_item"] = ("Ticket or task", "Tiket ili zadatak"),
        ["ts_task"] = ("Task", "Zadatak"),
        ["ts_ticket"] = ("Ticket", "Tiket"),
        ["ts_start"] = ("Start", "Početak"),
        ["ts_end"] = ("End", "Kraj"),
        ["ts_running"] = ("still running", "još traje"),
        ["ts_tasksNoCompany"] = ("Tasks (no company)", "Zadaci (bez firme)"),
        ["ts_wd0"] = ("Sun", "Ned"),
        ["ts_wd1"] = ("Mon", "Pon"),
        ["ts_wd2"] = ("Tue", "Uto"),
        ["ts_wd3"] = ("Wed", "Sri"),
        ["ts_wd4"] = ("Thu", "Čet"),
        ["ts_wd5"] = ("Fri", "Pet"),
        ["ts_wd6"] = ("Sat", "Sub"),
    };

    public static Func<string, string> For(string? language) =>
        key => Words.TryGetValue(key, out var w) ? EmailLanguage.Pick(language, w.En, w.Bs) : key;
}
