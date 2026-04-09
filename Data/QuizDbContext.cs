using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using QuizAPI.Models;

namespace QuizAPI.Data
{
    public class QuizDbContext : IdentityDbContext<AppUser>
    {
        public QuizDbContext(DbContextOptions<QuizDbContext> options) : base(options) { }

        public DbSet<Quiz> Quizzes => Set<Quiz>();
        public DbSet<Question> Questions => Set<Question>();
        public DbSet<Answer> Answers => Set<Answer>();
        public DbSet<Image> Images => Set<Image>();
        public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();
        public DbSet<PreEmploymentSubmission> PreEmploymentSubmissions => Set<PreEmploymentSubmission>();
        public DbSet<QuizProgress> QuizProgressEntries => Set<QuizProgress>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder); // <-- critical line

            builder.Entity<Quiz>()
                .HasMany(q => q.Questions)
                .WithOne(qn => qn.Quiz!)
                .HasForeignKey(qn => qn.QuizId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Question>()
                .HasMany(qn => qn.Answers)
                .WithOne(a => a.Question!)
                .HasForeignKey(a => a.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Question>()
                .HasMany(qn => qn.Images)
                .WithOne()
                .HasForeignKey(i => i.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<QuizAttempt>()
                .HasOne(qa => qa.User)
                .WithMany()
                .HasForeignKey(qa => qa.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<QuizProgress>()
                .HasOne(qp => qp.User)
                .WithMany()
                .HasForeignKey(qp => qp.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
