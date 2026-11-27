using TicketSystemTech.Api.Startup;

namespace TicketSystemTech.Tests;

/// <summary>What an import package must satisfy before anything is written (the part of --import-package that needs no database).</summary>
public class LegacyPackageValidatorTests
{
    private static readonly DateTime Start = new(2024, 3, 1, 8, 0, 0, DateTimeKind.Utc);

    private static LegacyPackage Package() => new()
    {
        Companies = [new("o:1", "Firma", null, null, false), new("g:n", "Neodređeni", null, null, false)],
        Departments = [new("d:1", "Softver", "software@example.test")],
        HelpTopics = [new("t:1", "Greška")],
        SlaPlans = [new("l:1", "Default", 48)],
        Categories = [new("k:f", "Fakturisano")],
        Users =
        [
            new("c:1", "Client", "klijent@example.test", "Ana", "Anić", null, "o:1", null, true, null),
            new("s:1", "Employee", "osoblje@example.test", "Boris", "Borić", null, null, "d:1", true, null),
        ],
    };

    private static PackageTicket Ticket(string number = "123456") => new(
        number, "Naslov", "<p>Tekst</p>", "Closed", "Email", "Medium", "c:1", "o:1", "d:1", "t:1", "l:1", null, Start, Start.AddHours(1), null, Start.AddDays(1), "s:1",
        "Riješeno.", [new("s:1", Start.AddHours(1), true)], [new("c:1", "Response", "<p>odgovor</p>", Start.AddHours(2)), new("s:1", "InternalNote", "<p>bilješka</p>", Start.AddHours(3))]);

    private static IReadOnlyList<string> Problems(LegacyPackage package, params PackageTicket[] tickets) => LegacyPackageValidator.Validate(package, tickets).Problems;

    [Fact]
    public void A_package_that_is_in_order_has_no_problems_and_is_counted()
    {
        var outcome = LegacyPackageValidator.Validate(Package(), [Ticket("1"), Ticket("2")]);

        Assert.Empty(outcome.Problems);
        Assert.Equal(2, outcome.Tickets);
        Assert.Equal(4, outcome.Messages);
        Assert.Equal(2, outcome.Assignees);
    }

    [Fact]
    public void A_ticket_that_is_not_in_the_company_of_the_person_who_wrote_it_is_refused()
    {
        var problems = Problems(Package(), Ticket() with { CompanyKey = "g:n" });

        Assert.Contains(problems, p => p.Contains("not the company of its client"));
    }

    [Fact]
    public void A_ticket_needs_a_company_that_exists()
    {
        Assert.Contains(Problems(Package(), Ticket() with { CompanyKey = "o:404" }), p => p.Contains("the company 'o:404' does not exist"));
    }

    [Fact]
    public void A_number_used_twice_or_too_long_is_refused()
    {
        Assert.Contains(Problems(Package(), Ticket("100"), Ticket("100")), p => p.Contains("used twice"));
        Assert.Contains(Problems(Package(), Ticket(new string('9', 21))), p => p.Contains("longer than 20"));
    }

    [Fact]
    public void A_closed_ticket_has_a_time_of_closing_and_a_resolution_and_an_open_one_has_no_time_of_closing()
    {
        Assert.Contains(Problems(Package(), Ticket() with { ClosedAtUtc = null }), p => p.Contains("without the time it was closed"));
        Assert.Contains(Problems(Package(), Ticket() with { ResolutionSummary = "  " }), p => p.Contains("without a description of the resolution"));
        Assert.Contains(Problems(Package(), Ticket() with { Status = "Open" }), p => p.Contains("not closed, but has a time of closing"));
    }

    [Fact]
    public void A_client_needs_a_company_and_an_employee_needs_a_branch()
    {
        var package = Package();
        package.Users.Add(new("c:2", "Client", "drugi@example.test", "Dino", "Dinić", null, null, null, true, null));
        package.Users.Add(new("s:2", "Employee", "treci@example.test", "Emir", "Emirić", null, null, "d:404", true, null));

        var problems = Problems(package);

        Assert.Contains(problems, p => p.Contains("person c:2") && p.Contains("client needs a company"));
        Assert.Contains(problems, p => p.Contains("person s:2") && p.Contains("employee needs a branch"));
    }

    [Fact]
    public void An_address_used_twice_whatever_its_case_and_a_branch_address_used_twice_are_refused()
    {
        var package = Package();
        package.Users.Add(new("c:2", "Client", "KLIJENT@example.test", "Dino", "Dinić", null, "o:1", null, true, null));
        package.Departments.Add(new("d:2", "Servis", "SOFTWARE@example.test"));

        var problems = Problems(package);

        Assert.Contains(problems, p => p.Contains("person c:2") && p.Contains("someone else has it"));
        Assert.Contains(problems, p => p.Contains("branch d:2") && p.Contains("each branch needs its own"));
    }

    [Fact]
    public void Something_a_ticket_points_at_must_exist()
    {
        var problems = Problems(Package(), Ticket() with { HelpTopicKey = "t:404", SlaKey = "l:404", CategoryKey = "k:404", DepartmentKey = "d:404", Source = "Fax", Priority = "Urgent" });

        Assert.Contains(problems, p => p.Contains("help topic 't:404'"));
        Assert.Contains(problems, p => p.Contains("SLA plan 'l:404'"));
        Assert.Contains(problems, p => p.Contains("category 'k:404'"));
        Assert.Contains(problems, p => p.Contains("branch 'd:404'"));
        Assert.Contains(problems, p => p.Contains("source 'Fax'"));
        Assert.Contains(problems, p => p.Contains("priority 'Urgent'"));
    }

    [Fact]
    public void Only_staff_can_be_assigned_or_close_a_ticket_and_a_client_cannot_write_a_note()
    {
        var ticket = Ticket() with
        {
            ClosedByKey = "c:1",
            Assignees = [new("c:1", Start, true), new("s:1", Start, true)],
            Messages = [new("c:1", "InternalNote", "<p>x</p>", Start), new("s:404", "Response", "<p>x</p>", Start), new("s:1", "SystemEvent", "<p>x</p>", Start)],
        };

        var problems = Problems(Package(), ticket);

        Assert.Contains(problems, p => p.Contains("closed by 'c:1'"));
        Assert.Contains(problems, p => p.Contains("assigned to 'c:1'"));
        Assert.Contains(problems, p => p.Contains("more than one main assignee"));
        Assert.Contains(problems, p => p.Contains("an internal note by a client"));
        Assert.Contains(problems, p => p.Contains("'s:404', who is not in the package"));
        Assert.Contains(problems, p => p.Contains("type 'SystemEvent'"));
    }

    [Fact]
    public void A_long_list_of_problems_is_cut_and_says_how_many_were_left_out()
    {
        var tickets = Enumerable.Range(0, 100).Select(i => Ticket(i.ToString()) with { CompanyKey = "g:n" }).ToArray();

        var problems = Problems(Package(), tickets);

        Assert.Equal(41, problems.Count);
        Assert.Equal("... and 60 more problem(s).", problems[^1]);
    }
}
