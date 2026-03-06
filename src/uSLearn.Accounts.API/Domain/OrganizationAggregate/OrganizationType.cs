using System.Text.Json.Serialization;

namespace uSLearn.Accounts.Domain.OrganizationAggregate;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OrganizationType
{
    Company = 1,
    BusinessGroup = 2,
    University = 3,
    Facility = 4,
    RDInstitution = 5,
    Association = 6,
    Agency = 7,
    Authority = 8,
    Government = 9,
    Consulting = 10,
}

/*
    Company
    BusinessGroup
    University
    Facility
    Research and Development Institution
    Association
    Agency
    Authority
    Government
    Consulting
 */

