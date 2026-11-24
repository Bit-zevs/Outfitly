using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Outfitly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReleasePendingUsernames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Match Usernames.Pending: the UUID stays unique, and '~' is outside the public grammar.
            migrationBuilder.Sql("""
                UPDATE "Users" AS u
                SET "Username" = '~' || translate(rtrim(encode(uuid_send(u."Id"), 'base64'), '='), '+/', '-_')
                FROM "Accounts" AS a
                WHERE a."UserId" = u."Id" AND NOT a."EmailConfirmed";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Released names may already belong to someone else. Assign valid generated names for an older API.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    pending record;
                    candidate text;
                    attempt integer;
                BEGIN
                    FOR pending IN SELECT "Id" FROM "Users" WHERE "Username" LIKE '~%' LOOP
                        attempt := 0;
                        LOOP
                            candidate := 'u_' || left(md5(pending."Id"::text || ':' || attempt::text), 28);
                            EXIT WHEN NOT EXISTS (SELECT 1 FROM "Users" WHERE "Username" = candidate);
                            attempt := attempt + 1;
                        END LOOP;
                        UPDATE "Users" SET "Username" = candidate WHERE "Id" = pending."Id";
                    END LOOP;
                END $$;
                """);
        }
    }
}
