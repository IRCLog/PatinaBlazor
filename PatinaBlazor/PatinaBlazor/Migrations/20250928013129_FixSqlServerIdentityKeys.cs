using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatinaBlazor.Migrations
{
    /// <inheritdoc />
    public partial class FixSqlServerIdentityKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only apply these fixes for SQL Server
            if (migrationBuilder.ActiveProvider?.Contains("SqlServer") == true)
            {
                // Fix Identity table column lengths for SQL Server 2022 compatibility
                migrationBuilder.Sql(@"
                    -- Fix AspNetRoles
                    ALTER TABLE AspNetRoles ALTER COLUMN Id NVARCHAR(128) NOT NULL;
                    ALTER TABLE AspNetRoles ALTER COLUMN Name NVARCHAR(256) NULL;
                    ALTER TABLE AspNetRoles ALTER COLUMN NormalizedName NVARCHAR(256) NULL;

                    -- Fix AspNetUsers
                    ALTER TABLE AspNetUsers ALTER COLUMN Id NVARCHAR(128) NOT NULL;
                    ALTER TABLE AspNetUsers ALTER COLUMN UserName NVARCHAR(256) NULL;
                    ALTER TABLE AspNetUsers ALTER COLUMN NormalizedUserName NVARCHAR(256) NULL;
                    ALTER TABLE AspNetUsers ALTER COLUMN Email NVARCHAR(256) NULL;
                    ALTER TABLE AspNetUsers ALTER COLUMN NormalizedEmail NVARCHAR(256) NULL;

                    -- Fix foreign key columns
                    ALTER TABLE AspNetUserRoles ALTER COLUMN UserId NVARCHAR(128) NOT NULL;
                    ALTER TABLE AspNetUserRoles ALTER COLUMN RoleId NVARCHAR(128) NOT NULL;

                    ALTER TABLE AspNetUserClaims ALTER COLUMN UserId NVARCHAR(128) NOT NULL;

                    ALTER TABLE AspNetUserLogins ALTER COLUMN UserId NVARCHAR(128) NOT NULL;
                    ALTER TABLE AspNetUserLogins ALTER COLUMN LoginProvider NVARCHAR(128) NOT NULL;
                    ALTER TABLE AspNetUserLogins ALTER COLUMN ProviderKey NVARCHAR(128) NOT NULL;

                    ALTER TABLE AspNetRoleClaims ALTER COLUMN RoleId NVARCHAR(128) NOT NULL;

                    ALTER TABLE AspNetUserTokens ALTER COLUMN UserId NVARCHAR(128) NOT NULL;
                    ALTER TABLE AspNetUserTokens ALTER COLUMN LoginProvider NVARCHAR(128) NOT NULL;
                    ALTER TABLE AspNetUserTokens ALTER COLUMN Name NVARCHAR(128) NOT NULL;
                ");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
