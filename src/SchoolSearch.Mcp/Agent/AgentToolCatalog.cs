using System.Text.Json;

namespace SchoolSearch.Mcp.Agent;

/// <summary>
/// The tools the agent may call: the read-only MCP school, preschool, methodology and reviews tools. Names and argument
/// names match the MCP surface. The agent runs them in-process (<see cref="AgentToolRunner"/>), never over HTTP to itself.
/// </summary>
public static class AgentToolCatalog
{
    public static IReadOnlyList<AgentToolDef> Definitions { get; } =
    [
        Define(
            "find_schools",
            "Finds primary schools by name, gmina, public status or distance from a point. Sorts by name (default) or by score (descending, unscored last). There is no ascending option.",
            [
                ("query", StringProp("Case-insensitive part of the school name, at least 2 characters.")),
                ("gmina", StringProp("Gmina TERYT code, 7 digits.")),
                ("isPublic", BoolProp("true for public schools, false for non-public schools.")),
                ("lat", NumberProp("Latitude of the search centre. Only with lon.")),
                ("lon", NumberProp("Longitude of the search centre. Only with lat.")),
                ("radiusKm", NumberProp("Search radius in km, up to 50.")),
                ("sort", ChoiceProp("'name' (default) or 'score'.", "name", "score")),
                ("page", IntegerProp("Page number, starting at 1.")),
                ("pageSize", IntegerProp("Items per page, 1 to 50.")),
            ]),
        Define(
            "get_school",
            "Returns one school profile by its RSPO identifier: governing body type, cohorts, score trend by exam year with coverage, and provenance.",
            [("rspo", IntegerProp("RSPO identifier of the school (positive integer)."))],
            "rspo"),
        Define(
            "get_catchment_school",
            "Answers which school's catchment (obwód) covers an address, according to the gmina's published street list. 'no_data' means the gmina published no list; it is not a school and must not be guessed from distance.",
            [
                ("gmina", StringProp("Gmina TERYT code, 7 digits.")),
                ("street", StringProp("Street name, for example 'ul. Przykładowa'.")),
                ("number", StringProp("House number, for example '12' or '12A'.")),
            ],
            "gmina",
            "street"),
        Define(
            "compare_schools",
            "Returns the profiles of 2 to 4 schools side by side, given their slugs (as returned by find_schools).",
            [("slugs", ArrayProp("Two to four school slugs."))],
            "slugs"),
        Define(
            "get_ranking",
            "Returns the published ranking for the country or for one gmina and one exam year. Descending by score only. Suppressed schools are counted, never shown as zero. This is not an official ranking.",
            [
                ("scope", ChoiceProp("'country' or 'gmina'.", "country", "gmina")),
                ("gmina", StringProp("Gmina TERYT code, 7 digits. Required when scope is 'gmina'.")),
                ("year", IntegerProp("Exam year. Omit for the latest published year.")),
                ("page", IntegerProp("Page number, starting at 1.")),
                ("pageSize", IntegerProp("Items per page, 1 to 50.")),
            ],
            "scope"),
        Define(
            "get_methodology",
            "Returns the current scoring methodology: version, publication threshold, coverage floor, weights, interval definition and the documentation link. Use it to explain what a score means.",
            []),
        Define(
            "find_preschools",
            "Finds preschools by name, gmina, public status, monthly tuition, free places or distance from a point. Sorted by name. Preschools have no quality score.",
            [
                ("q", StringProp("Case-insensitive part of the preschool name, at least 2 characters.")),
                ("gmina", StringProp("Gmina TERYT code, 7 digits.")),
                ("publicOnly", BoolProp("true for public preschools only, false for non-public only.")),
                ("maxTuition", NumberProp("Highest monthly tuition in PLN, zero or more.")),
                ("hasFreePlaces", BoolProp("true for preschools that report free places.")),
                ("lat", NumberProp("Latitude of the search centre. Only with lon.")),
                ("lon", NumberProp("Longitude of the search centre. Only with lat.")),
                ("radiusKm", NumberProp("Search radius in km, up to 50.")),
                ("page", IntegerProp("Page number, starting at 1.")),
                ("pageSize", IntegerProp("Items per page, 1 to 50.")),
            ]),
        Define(
            "get_preschool",
            "Returns one preschool profile by its slug: address, public or non-public, governing body type, tuition range, hours and free places, each with source and date.",
            [("slug", StringProp("Preschool slug, as returned by find_preschools."))],
            "slug"),
        Define(
            "get_school_reviews",
            "Returns one page of the published user-generated opinions (reviews) of one school, with the school's public replies. Opinions are user content, not facts and not a score.",
            [
                ("slug", StringProp("School slug, as returned by find_schools.")),
                ("page", IntegerProp("Page number, starting at 1.")),
                ("pageSize", IntegerProp("Items per page, 1 to 50.")),
            ],
            "slug"),
    ];

    private static AgentToolDef Define(string name, string description, (string Name, object Schema)[] properties, params string[] required)
    {
        var schema = properties.ToDictionary(p => p.Name, p => JsonSerializer.SerializeToElement(p.Schema));
        return new AgentToolDef(name, description, schema, required);
    }

    private static object StringProp(string description) => new { type = "string", description };

    private static object IntegerProp(string description) => new { type = "integer", description };

    private static object NumberProp(string description) => new { type = "number", description };

    private static object BoolProp(string description) => new { type = "boolean", description };

    private static object ChoiceProp(string description, params string[] values) =>
        new { type = "string", description, @enum = values };

    private static object ArrayProp(string description) =>
        new { type = "array", description, items = new { type = "string" } };
}
