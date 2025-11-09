using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UserRoles.Data;
using UserRoles.Models;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Http;

namespace UserRoles.Controllers
{
    public class StudentController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public StudentController(AppDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        // GET: Student/EnrollmentForm
        public async Task<IActionResult> EnrollmentForm()
        {
            // Check if user is logged in
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account");
            }

            // Check if already enrolled
            var existingEnrollment = await _context.Enrollments
                .FirstOrDefaultAsync(e => e.UserId == userId);

            if (existingEnrollment != null)
            {
                return RedirectToAction("ViewEnrollment");
            }

            return View();
        }

        // POST: Student/ProcessEnrollment
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessEnrollment(Enrollment enrollment, IFormFile birthCertFile, IFormFile form137File)
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account");
            }

            try
            {
                // Upload birth certificate
                var birthCertPath = await UploadFile(birthCertFile, "birth_certificates");
                if (birthCertPath == null)
                {
                    TempData["Error"] = "Birth Certificate upload failed. Only PDF files under 5MB allowed.";
                    return RedirectToAction("EnrollmentForm");
                }

                // Upload form137
                var form137Path = await UploadFile(form137File, "form137");
                if (form137Path == null)
                {
                    TempData["Error"] = "Form 137 upload failed. Only PDF files under 5MB allowed.";
                    return RedirectToAction("EnrollmentForm");
                }

                // Assign section
                var section = await AssignSection(enrollment.GradeLevel);

                // Save enrollment
                enrollment.UserId = userId;
                enrollment.BirthCertificate = birthCertPath;
                enrollment.Form137 = form137Path;
                enrollment.Section = section;
                enrollment.EnrollmentDate = DateTime.Now;
                enrollment.Status = "pending";

                _context.Enrollments.Add(enrollment);
                await _context.SaveChangesAsync();

                TempData["Success"] = $"Enrollment submitted successfully! You are assigned to Section {section}.";
                return RedirectToAction("ViewEnrollment");
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error: {ex.Message}";
                return RedirectToAction("EnrollmentForm");
            }
        }

        // GET: Student/ViewEnrollment
        public async Task<IActionResult> ViewEnrollment()
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account");
            }

            var enrollment = await _context.Enrollments
                .FirstOrDefaultAsync(e => e.UserId == userId);

            if (enrollment == null)
            {
                return RedirectToAction("EnrollmentForm");
            }

            return View(enrollment);
        }

        // GET: Student/PrintEnrollment
        public async Task<IActionResult> PrintEnrollment()
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account");
            }

            var enrollment = await _context.Enrollments
                .FirstOrDefaultAsync(e => e.UserId == userId);

            if (enrollment == null)
            {
                return RedirectToAction("EnrollmentForm");
            }

            return View(enrollment);
        }

        // Helper method to upload files
        private async Task<string> UploadFile(IFormFile file, string folder)
        {
            if (file == null || file.Length == 0)
                return null;

            // Check file size (5MB max)
            if (file.Length > 5 * 1024 * 1024)
                return null;

            // Check file type (PDF only)
            if (file.ContentType != "application/pdf")
                return null;

            var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", folder);
            Directory.CreateDirectory(uploadsFolder);

            var uniqueFileName = $"{Guid.NewGuid()}_{file.FileName}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            return $"/uploads/{folder}/{uniqueFileName}";
        }

        // Helper method to assign section
        private async Task<int> AssignSection(string gradeLevel)
        {
            var capacity = await _context.SectionCapacities
                .FirstOrDefaultAsync(sc => sc.GradeLevel == gradeLevel);

            if (capacity == null)
            {
                // Create new capacity record
                capacity = new SectionCapacity
                {
                    GradeLevel = gradeLevel,
                    CurrentSection = 1,
                    StudentsInCurrentSection = 0
                };
                _context.SectionCapacities.Add(capacity);
            }

            // Check if current section is full
            if (capacity.StudentsInCurrentSection >= 35)
            {
                capacity.CurrentSection++;
                capacity.StudentsInCurrentSection = 0;
            }

            capacity.StudentsInCurrentSection++;
            await _context.SaveChangesAsync();

            return capacity.CurrentSection;
        }

        public async Task<IActionResult> EnrollmentProgress()
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account");
            }

            var enrollment = await _context.Enrollments
                .Include(e => e.User)
                .FirstOrDefaultAsync(e => e.UserId == userId);

            if (enrollment == null)
            {
                return RedirectToAction("EnrollmentForm");
            }

            return View(enrollment);
        }
    }
}