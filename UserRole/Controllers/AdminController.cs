using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UserRoles.Data;
using UserRoles.Models;
using UserRoles.ViewModels;
using UserRole.Services;

namespace UserRoles.Controllers
{
    [Authorize]
    public class AdminController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<Users> _userManager;
        private readonly IEmailServices _emailService;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            AppDbContext context,
            UserManager<Users> userManager,
            IEmailServices emailService,
            ILogger<AdminController> logger)
        {
            _context = context;
            _userManager = userManager;
            _emailService = emailService;
            _logger = logger;
        }

        // Dashboard with statistics
        public async Task<IActionResult> Dashboard()
        {
            var role = HttpContext.Session.GetString("Role");
            if (role != "admin")
            {
                return RedirectToAction("Index", "Home");
            }

            var viewModel = new AdminDashboardViewModel
            {
                TotalStudents = await _context.Enrollments.CountAsync(e => e.Status == "approved"),
                PendingEnrollments = await _context.Enrollments.CountAsync(e => e.Status == "pending"),
                TotalProfessors = await _context.Users.CountAsync(u => u.Role == "professor"),
                TotalSections = 48, // 8 sections per grade × 6 grades

                // Students per grade level
                StudentsPerGradeLevel = await _context.Enrollments
                    .Where(e => e.Status == "approved")
                    .GroupBy(e => e.GradeLevel)
                    .Select(g => new GradeLevelStats
                    {
                        GradeLevel = g.Key,
                        StudentCount = g.Count()
                    })
                    .OrderBy(g => g.GradeLevel)
                    .ToListAsync(),

                // Professors per grade level
                ProfessorsPerGradeLevel = await _context.Users
                    .Where(u => u.Role == "professor")
                    .GroupBy(u => u.AssignedGradeLevel)
                    .Select(g => new GradeLevelStats
                    {
                        GradeLevel = g.Key,
                        ProfessorCount = g.Count()
                    })
                    .OrderBy(g => g.GradeLevel)
                    .ToListAsync(),

                // Recent enrollments
                RecentEnrollments = await _context.Enrollments
                    .Include(e => e.User)
                    .OrderByDescending(e => e.EnrollmentDate)
                    .Take(5)
                    .ToListAsync()
            };

            return View(viewModel);
        }

        // View all students
        public async Task<IActionResult> ViewStudents(string status = "all")
        {
            var role = HttpContext.Session.GetString("Role");
            if (role != "admin")
            {
                return RedirectToAction("Index", "Home");
            }

            IQueryable<Enrollment> query = _context.Enrollments.Include(e => e.User);

            if (status != "all")
            {
                query = query.Where(e => e.Status == status);
            }

            var students = await query.OrderByDescending(e => e.EnrollmentDate).ToListAsync();
            ViewBag.CurrentFilter = status;

            return View(students);
        }

        // Accept enrollment
        [HttpPost]
        public async Task<IActionResult> AcceptEnrollment(int id)
        {
            var enrollment = await _context.Enrollments
                .Include(e => e.User)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (enrollment == null)
            {
                return NotFound();
            }

            enrollment.Status = "approved";
            await _context.SaveChangesAsync();

            // Send acceptance email
            try
            {
                string subject = "Enrollment Approved - Elementary School";
                string htmlMessage = $@"
                    <html>
                    <body style='font-family: Arial, sans-serif; padding: 20px;'>
                        <div style='max-width: 600px; margin: 0 auto; background: white; border-radius: 10px; padding: 30px; box-shadow: 0 4px 6px rgba(0,0,0,0.1);'>
                            <h2 style='color: #28a745; text-align: center;'>✅ Enrollment Approved!</h2>
                            
                            <p>Dear Parent/Guardian,</p>
                            
                            <p>We are pleased to inform you that the enrollment application for <strong>{enrollment.StudentName}</strong> has been approved.</p>
                            
                            <div style='background: #f8f9fa; padding: 20px; border-radius: 8px; margin: 20px 0;'>
                                <h3 style='margin-top: 0; color: #333;'>Enrollment Details:</h3>
                                <p><strong>Student Name:</strong> {enrollment.StudentName}</p>
                                <p><strong>Grade Level:</strong> Grade {enrollment.GradeLevel}</p>
                                <p><strong>Section:</strong> Section {enrollment.Section}</p>
                                <p><strong>School Year:</strong> {DateTime.Now.Year} - {DateTime.Now.Year + 1}</p>
                            </div>
                            
                            <p>Please log in to your account to view and print your enrollment confirmation.</p>
                            
                            <div style='text-align: center; margin-top: 30px;'>
                                <a href='https://localhost:7065/Student/ViewEnrollment' style='background: #007bff; color: white; padding: 12px 30px; text-decoration: none; border-radius: 5px; display: inline-block;'>View Enrollment</a>
                            </div>
                            
                            <p style='margin-top: 30px; color: #666; font-size: 14px;'>
                                If you have any questions, please contact us at:<br>
                                📞 (02) 239 8307<br>
                                ✉️ fortbonifacio01@gmail.com
                            </p>
                        </div>
                    </body>
                    </html>";

                await _emailService.SendEmailAsync(enrollment.User.Email, subject, htmlMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send acceptance email");
            }

            TempData["Success"] = $"Enrollment for {enrollment.StudentName} has been approved and email sent.";
            return RedirectToAction(nameof(ViewStudents));
        }

        // Decline enrollment
        [HttpPost]
        public async Task<IActionResult> DeclineEnrollment(int id)
        {
            var enrollment = await _context.Enrollments
                .Include(e => e.User)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (enrollment == null)
            {
                return NotFound();
            }

            enrollment.Status = "rejected";
            await _context.SaveChangesAsync();

            // Send rejection email
            try
            {
                string subject = "Enrollment Application Update - Elementary School";
                string htmlMessage = $@"
                    <html>
                    <body style='font-family: Arial, sans-serif; padding: 20px;'>
                        <div style='max-width: 600px; margin: 0 auto; background: white; border-radius: 10px; padding: 30px;'>
                            <h2 style='color: #dc3545; text-align: center;'>Enrollment Application Update</h2>
                            
                            <p>Dear Parent/Guardian,</p>
                            
                            <p>We regret to inform you that the enrollment application for <strong>{enrollment.StudentName}</strong> could not be approved at this time.</p>
                            
                            <p>For more information or to resubmit your application, please contact our enrollment office.</p>
                            
                            <p style='margin-top: 30px; color: #666;'>
                                Contact us at:<br>
                                📞 (02) 239 8307<br>
                                ✉️ fortbonifacio01@gmail.com
                            </p>
                        </div>
                    </body>
                    </html>";

                await _emailService.SendEmailAsync(enrollment.User.Email, subject, htmlMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send rejection email");
            }

            TempData["Info"] = $"Enrollment for {enrollment.StudentName} has been declined.";
            return RedirectToAction(nameof(ViewStudents));
        }

        // Professor Management
        public async Task<IActionResult> ManageProfessors()
        {
            var role = HttpContext.Session.GetString("Role");
            if (role != "admin")
            {
                return RedirectToAction("Index", "Home");
            }

            var professors = await _context.Users
                .Where(u => u.Role == "professor")
                .OrderBy(u => u.AssignedGradeLevel)
                .ThenBy(u => u.AssignedSection)
                .ToListAsync();

            return View(professors);
        }

        // Add Professor - GET
        public IActionResult AddProfessor()
        {
            var role = HttpContext.Session.GetString("Role");
            if (role != "admin")
            {
                return RedirectToAction("Index", "Home");
            }

            return View();
        }

        // Add Professor - POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddProfessor(AddProfessorViewModel model)
        {
            if (ModelState.IsValid)
            {
                var user = new Users
                {
                    FullName = model.FullName,
                    UserName = model.Email,
                    Email = model.Email,
                    EmailConfirmed = true,
                    Role = "professor",
                    AssignedGradeLevel = model.GradeLevel,
                    AssignedSection = model.Section
                };

                var result = await _userManager.CreateAsync(user, model.Password);

                if (result.Succeeded)
                {
                    TempData["Success"] = $"Professor {model.FullName} has been added successfully.";
                    return RedirectToAction(nameof(ManageProfessors));
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            return View(model);
        }

        // Edit Professor - GET
        public async Task<IActionResult> EditProfessor(string id)
        {
            var role = HttpContext.Session.GetString("Role");
            if (role != "admin")
            {
                return RedirectToAction("Index", "Home");
            }

            var professor = await _userManager.FindByIdAsync(id);
            if (professor == null || professor.Role != "professor")
            {
                return NotFound();
            }

            var model = new EditProfessorViewModel
            {
                Id = professor.Id,
                FullName = professor.FullName,
                Email = professor.Email,
                GradeLevel = professor.AssignedGradeLevel,
                Section = professor.AssignedSection
            };

            return View(model);
        }

        // Edit Professor - POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProfessor(EditProfessorViewModel model)
        {
            if (ModelState.IsValid)
            {
                var professor = await _userManager.FindByIdAsync(model.Id);
                if (professor == null)
                {
                    return NotFound();
                }

                professor.FullName = model.FullName;
                professor.Email = model.Email;
                professor.UserName = model.Email;
                professor.AssignedGradeLevel = model.GradeLevel;
                professor.AssignedSection = model.Section;

                var result = await _userManager.UpdateAsync(professor);

                if (result.Succeeded)
                {
                    TempData["Success"] = $"Professor {model.FullName} has been updated successfully.";
                    return RedirectToAction(nameof(ManageProfessors));
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            return View(model);
        }

        // Delete Professor
        [HttpPost]
        public async Task<IActionResult> DeleteProfessor(string id)
        {
            var professor = await _userManager.FindByIdAsync(id);
            if (professor == null || professor.Role != "professor")
            {
                return NotFound();
            }

            var result = await _userManager.DeleteAsync(professor);

            if (result.Succeeded)
            {
                TempData["Success"] = $"Professor {professor.FullName} has been deleted successfully.";
            }
            else
            {
                TempData["Error"] = "Failed to delete professor.";
            }

            return RedirectToAction(nameof(ManageProfessors));
        }

        // Section Management
        public async Task<IActionResult> ManageSections()
        {
            var role = HttpContext.Session.GetString("Role");
            if (role != "admin")
            {
                return RedirectToAction("Index", "Home");
            }

            var viewModel = new SectionManagementViewModel();

            // Get all grade levels (1-6)
            for (int grade = 1; grade <= 6; grade++)
            {
                var gradeLevel = new GradeLevelSections
                {
                    GradeLevel = grade.ToString()
                };

                // Get students grouped by section for this grade
                var studentsInGrade = await _context.Enrollments
                    .Where(e => e.GradeLevel == grade.ToString() && e.Status == "approved")
                    .GroupBy(e => e.Section)
                    .Select(g => new SectionInfo
                    {
                        SectionNumber = g.Key ?? 0,
                        StudentCount = g.Count()
                    })
                    .ToListAsync();

                gradeLevel.Sections = studentsInGrade;
                gradeLevel.TotalStudents = studentsInGrade.Sum(s => s.StudentCount);

                viewModel.GradeLevels.Add(gradeLevel);
            }

            return View(viewModel);
        }

        // View Students in Specific Section
        public async Task<IActionResult> ViewSectionStudents(string gradeLevel, int section)
        {
            var role = HttpContext.Session.GetString("Role");
            if (role != "admin")
            {
                return RedirectToAction("Index", "Home");
            }

            var students = await _context.Enrollments
                .Include(e => e.User)
                .Where(e => e.GradeLevel == gradeLevel && e.Section == section && e.Status == "approved")
                .OrderBy(e => e.StudentName)
                .ToListAsync();

            ViewBag.GradeLevel = gradeLevel;
            ViewBag.Section = section;
            ViewBag.StudentCount = students.Count;

            return View(students);
        }

        // Class Schedules
        public IActionResult ClassSchedules()
        {
            var role = HttpContext.Session.GetString("Role");
            if (role != "admin")
            {
                return RedirectToAction("Index", "Home");
            }

            var schedules = new List<ClassScheduleViewModel>
            {
                new ClassScheduleViewModel
                {
                    GradeLevel = "1",
                    Schedule = "1:00 PM - 6:00 PM",
                    StartTime = "1:00 PM",
                    EndTime = "6:00 PM",
                    Shift = "Afternoon"
                },
                new ClassScheduleViewModel
                {
                    GradeLevel = "2",
                    Schedule = "7:00 AM - 12:00 PM",
                    StartTime = "7:00 AM",
                    EndTime = "12:00 PM",
                    Shift = "Morning"
                },
                new ClassScheduleViewModel
                {
                    GradeLevel = "3",
                    Schedule = "1:00 PM - 6:00 PM",
                    StartTime = "1:00 PM",
                    EndTime = "6:00 PM",
                    Shift = "Afternoon"
                },
                new ClassScheduleViewModel
                {
                    GradeLevel = "4",
                    Schedule = "7:00 AM - 12:00 PM",
                    StartTime = "7:00 AM",
                    EndTime = "12:00 PM",
                    Shift = "Morning"
                },
                new ClassScheduleViewModel
                {
                    GradeLevel = "5",
                    Schedule = "1:00 PM - 6:00 PM",
                    StartTime = "1:00 PM",
                    EndTime = "6:00 PM",
                    Shift = "Afternoon"
                },
                new ClassScheduleViewModel
                {
                    GradeLevel = "6",
                    Schedule = "7:00 AM - 12:00 PM",
                    StartTime = "7:00 AM",
                    EndTime = "12:00 PM",
                    Shift = "Morning"
                }
            };

            return View(schedules);
        }

        // Remove Student from Section (not delete enrollment)
        [HttpPost]
        public async Task<IActionResult> RemoveStudent(int id)
        {
            var enrollment = await _context.Enrollments.FindAsync(id);
            if (enrollment == null)
            {
                TempData["Error"] = "Student enrollment not found.";
                return RedirectToAction(nameof(ViewStudents));
            }

            var gradeLevel = enrollment.GradeLevel;
            var section = enrollment.Section;
            var studentName = enrollment.StudentName;

            // Remove student from section (set section to null)
            // Student remains enrolled but needs to be reassigned to a section
            enrollment.Section = null;
            enrollment.Status = "pending"; // Set back to pending for reassignment

            await _context.SaveChangesAsync();

            TempData["Success"] = $"Student {studentName} has been removed from Grade {gradeLevel} - Section {section}. They can be reassigned to another section.";

            if (gradeLevel != null && section.HasValue)
            {
                return RedirectToAction(nameof(ViewSectionStudents), new { gradeLevel, section });
            }

            return RedirectToAction(nameof(ViewStudents));
        }

        // Reassign Student to Section - GET
        public async Task<IActionResult> ReassignStudent(int id)
        {
            var role = HttpContext.Session.GetString("Role");
            if (role != "admin")
            {
                return RedirectToAction("Index", "Home");
            }

            var enrollment = await _context.Enrollments
                .Include(e => e.User)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (enrollment == null)
            {
                TempData["Error"] = "Student enrollment not found.";
                return RedirectToAction(nameof(ViewStudents));
            }

            
            var gradeLevel = enrollment.GradeLevel;
            var sectionsWithCapacity = new List<int>();
            var sectionCapacityData = new Dictionary<int, int>(); 

            
            for (int i = 1; i <= 8; i++)
            {
                var studentsInSection = await _context.Enrollments
                    .CountAsync(e => e.GradeLevel == gradeLevel &&
                                   e.Section == i &&
                                   e.Status == "approved");

                sectionCapacityData[i] = studentsInSection;

                if (studentsInSection < 40) 
                {
                    sectionsWithCapacity.Add(i);
                }
            }

            ViewBag.AvailableSections = sectionsWithCapacity;
            ViewBag.SectionCapacity = sectionCapacityData; 
            ViewBag.GradeLevel = gradeLevel;

            return View(enrollment);
        }

        [HttpPost]
        public async Task<IActionResult> ReassignStudent(int id, int newSection)
        {
            var enrollment = await _context.Enrollments.FindAsync(id);
            if (enrollment == null)
            {
                TempData["Error"] = "Student enrollment not found.";
                return RedirectToAction(nameof(ViewStudents));
            }

            
            var studentsInSection = await _context.Enrollments
                .CountAsync(e => e.GradeLevel == enrollment.GradeLevel &&
                               e.Section == newSection &&
                               e.Status == "approved");

            if (studentsInSection >= 40)
            {
                TempData["Error"] = $"Section {newSection} is full (40/40 students). Please choose another section.";
                return RedirectToAction(nameof(ReassignStudent), new { id });
            }

            var oldSection = enrollment.Section;
            enrollment.Section = newSection;
            enrollment.Status = "approved";
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Student {enrollment.StudentName} has been reassigned from Section {oldSection} to Section {newSection}.";

            return RedirectToAction(nameof(ViewSectionStudents), new { gradeLevel = enrollment.GradeLevel, section = newSection });
        }
    }
}