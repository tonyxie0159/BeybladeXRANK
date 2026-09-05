using BeybladeRecordSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BeybladeRecordSystem.Data;

public static class BeybladeQueries
{
    public static IQueryable<Beyblade> WithConfiguration(this IQueryable<Beyblade> query) =>
        query.Include(x => x.Configurations).ThenInclude(x => x.Parts).ThenInclude(x => x.Part);
}
