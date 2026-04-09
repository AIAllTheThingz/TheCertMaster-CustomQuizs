using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;


namespace QuizAPI
{
    public class UploadRequest
    {
        [Required]
        public IFormFile File { get; set; } = default!;
    }
}