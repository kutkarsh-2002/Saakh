using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saakh.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DealProposalsAndOverdueClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ClosedOverdue",
                table: "Deals",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OverdueWarningSentAt",
                table: "Deals",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DealProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InterestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProposedByProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    CategorySubTypeId = table.Column<int>(type: "int", nullable: true),
                    Capacity = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CapacityUnit = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    MaterialDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    EstimatedSettlementTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SupersededByProposalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DealId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RespondedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DealProposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DealProposals_CategorySubTypes_CategorySubTypeId",
                        column: x => x.CategorySubTypeId,
                        principalTable: "CategorySubTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DealProposals_DealProposals_SupersededByProposalId",
                        column: x => x.SupersededByProposalId,
                        principalTable: "DealProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DealProposals_Deals_DealId",
                        column: x => x.DealId,
                        principalTable: "Deals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DealProposals_Interests_InterestId",
                        column: x => x.InterestId,
                        principalTable: "Interests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DealProposals_Profiles_ProposedByProfileId",
                        column: x => x.ProposedByProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DealProposals_CategorySubTypeId",
                table: "DealProposals",
                column: "CategorySubTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_DealProposals_DealId",
                table: "DealProposals",
                column: "DealId");

            migrationBuilder.CreateIndex(
                name: "IX_DealProposals_InterestId_Status",
                table: "DealProposals",
                columns: new[] { "InterestId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DealProposals_ProposedByProfileId",
                table: "DealProposals",
                column: "ProposedByProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_DealProposals_SupersededByProposalId",
                table: "DealProposals",
                column: "SupersededByProposalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DealProposals");

            migrationBuilder.DropColumn(
                name: "ClosedOverdue",
                table: "Deals");

            migrationBuilder.DropColumn(
                name: "OverdueWarningSentAt",
                table: "Deals");
        }
    }
}
