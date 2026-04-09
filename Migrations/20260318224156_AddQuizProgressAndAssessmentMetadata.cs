using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuizAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddQuizProgressAndAssessmentMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "PassThresholdPercent",
                table: "Quizzes",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "Difficulty",
                table: "Questions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "Questions",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Passed",
                table: "PreEmploymentSubmissions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "PassingScorePercent",
                table: "PreEmploymentSubmissions",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.CreateTable(
                name: "QuizProgressEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SessionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    QuizId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuizTitle = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    QuizCategory = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    LaunchMode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    QuizDataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SelectionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CurrentIndex = table.Column<int>(type: "int", nullable: false),
                    TimerRemainingSeconds = table.Column<int>(type: "int", nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizProgressEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuizProgressEntries_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuizProgressEntries_UserId",
                table: "QuizProgressEntries",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuizProgressEntries");

            migrationBuilder.DropColumn(
                name: "PassThresholdPercent",
                table: "Quizzes");

            migrationBuilder.DropColumn(
                name: "Difficulty",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "Passed",
                table: "PreEmploymentSubmissions");

            migrationBuilder.DropColumn(
                name: "PassingScorePercent",
                table: "PreEmploymentSubmissions");
        }
    }
}
