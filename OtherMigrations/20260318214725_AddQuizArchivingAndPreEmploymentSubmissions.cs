using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuizAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddQuizArchivingAndPreEmploymentSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "Quizzes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PreEmploymentSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    QuizTitle = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SourceQuizTitles = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    TotalQuestions = table.Column<int>(type: "int", nullable: false),
                    CorrectCount = table.Column<int>(type: "int", nullable: false),
                    ScorePercent = table.Column<double>(type: "float", nullable: false),
                    SubmittedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreEmploymentSubmissions", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PreEmploymentSubmissions");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "Quizzes");
        }
    }
}
