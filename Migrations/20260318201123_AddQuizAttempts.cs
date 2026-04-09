using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuizAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddQuizAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuizAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    QuizId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuizTitle = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    QuizCategory = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TotalQuestions = table.Column<int>(type: "int", nullable: false),
                    CorrectCount = table.Column<int>(type: "int", nullable: false),
                    ScorePercent = table.Column<double>(type: "float", nullable: false),
                    SubmittedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuizAttempts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttempts_UserId",
                table: "QuizAttempts",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuizAttempts");
        }
    }
}
