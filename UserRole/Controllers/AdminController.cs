using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UserRole.Services;
using UserRoles.Data;
using UserRoles.Helpers;
using UserRoles.Models;
using UserRoles.ViewModels;

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

            // Get assigned professor
            var assignedProfessor = await _context.Users
                .FirstOrDefaultAsync(u => u.Role == "professor" &&
                                         u.AssignedGradeLevel == enrollment.GradeLevel &&
                                         u.AssignedSection == enrollment.Section);

            // Get room assignment
            string assignedRoom = null;
            if (assignedProfessor != null && !string.IsNullOrEmpty(assignedProfessor.AssignedRoom))
            {
                assignedRoom = assignedProfessor.AssignedRoom;
            }
            else
            {
                assignedRoom = RoomAssignmentHelper.GetRoomForSection(enrollment.GradeLevel, enrollment.Section.Value);
            }

            // Get class schedule
            var grade = int.Parse(enrollment.GradeLevel);
            bool isMorningShift = grade % 2 == 0; // Grades 2, 4, 6 = Morning
            string shift = isMorningShift ? "Morning" : "Afternoon";
            string classSchedule = isMorningShift ? "7:00 AM - 12:00 PM" : "1:00 PM - 6:00 PM";
            string professorName = assignedProfessor != null ? assignedProfessor.FullName : "To Be Assigned";
            string professorEmail = assignedProfessor != null ? assignedProfessor.Email : "TBA";

            // Send comprehensive acceptance email
            try
            {
                string subject = "🎉 Enrollment Approved - Fort Bonifacio Elementary School";
                string htmlMessage = $@"
            <html>
            <head>
                <style>
                    body {{
                        font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
                        line-height: 1.6;
                        color: #333;
                        background-color: #f5f5f5;
                        margin: 0;
                        padding: 0;
                    }}
                    .email-container {{
                        max-width: 650px;
                        margin: 20px auto;
                        background: white;
                        border-radius: 15px;
                        overflow: hidden;
                        box-shadow: 0 4px 15px rgba(0,0,0,0.1);
                    }}
                    .header {{
                        background: linear-gradient(135deg, #c8102e 0%, #003366 100%);
                        color: white;
                        padding: 40px 30px;
                        text-align: center;
                    }}
                    .header h1 {{
                        margin: 0;
                        font-size: 28px;
                        font-weight: 700;
                    }}
                    .header .emoji {{
                        font-size: 50px;
                        margin-bottom: 10px;
                    }}
                    .content {{
                        padding: 35px 30px;
                    }}
                    .greeting {{
                        font-size: 18px;
                        color: #1f2937;
                        margin-bottom: 20px;
                    }}
                    .success-message {{
                        background: #d1fae5;
                        border-left: 5px solid #10b981;
                        padding: 20px;
                        margin: 25px 0;
                        border-radius: 8px;
                    }}
                    .success-message h2 {{
                        margin: 0 0 10px 0;
                        color: #065f46;
                        font-size: 20px;
                    }}
                    .success-message p {{
                        margin: 0;
                        color: #047857;
                        font-size: 15px;
                    }}
                    .details-section {{
                        margin: 30px 0;
                    }}
                    .details-section h3 {{
                        color: #003366;
                        font-size: 20px;
                        margin-bottom: 20px;
                        border-bottom: 3px solid #c8102e;
                        padding-bottom: 10px;
                    }}
                    .detail-grid {{
                        display: grid;
                        grid-template-columns: 1fr 1fr;
                        gap: 15px;
                        margin-bottom: 20px;
                    }}
                    .detail-item {{
                        background: #f8fafc;
                        padding: 15px;
                        border-radius: 8px;
                        border-left: 3px solid #6366f1;
                    }}
                    .detail-item strong {{
                        display: block;
                        color: #64748b;
                        font-size: 12px;
                        text-transform: uppercase;
                        letter-spacing: 0.5px;
                        margin-bottom: 5px;
                    }}
                    .detail-item span {{
                        display: block;
                        color: #1f2937;
                        font-size: 16px;
                        font-weight: 600;
                    }}
                    .schedule-box {{
                        background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
                        color: white;
                        padding: 25px;
                        border-radius: 10px;
                        text-align: center;
                        margin: 25px 0;
                    }}
                    .schedule-box h3 {{
                        margin: 0 0 15px 0;
                        font-size: 18px;
                    }}
                    .schedule-box .time {{
                        font-size: 32px;
                        font-weight: 700;
                        margin: 15px 0;
                    }}
                    .schedule-box .shift {{
                        display: inline-block;
                        padding: 8px 20px;
                        background: rgba(255,255,255,0.25);
                        border-radius: 25px;
                        font-weight: 600;
                        margin-top: 10px;
                    }}
                    .important-info {{
                        background: #fef3c7;
                        border-left: 5px solid #f59e0b;
                        padding: 20px;
                        margin: 25px 0;
                        border-radius: 8px;
                    }}
                    .important-info h3 {{
                        margin: 0 0 10px 0;
                        color: #92400e;
                        font-size: 18px;
                    }}
                    .important-info ul {{
                        margin: 10px 0;
                        padding-left: 20px;
                        color: #78350f;
                    }}
                    .important-info li {{
                        margin: 8px 0;
                    }}
                    .button-container {{
                        text-align: center;
                        margin: 30px 0;
                    }}
                    .button {{
                        display: inline-block;
                        background: #c8102e;
                        color: white;
                        padding: 15px 40px;
                        text-decoration: none;
                        border-radius: 8px;
                        font-weight: 700;
                        font-size: 16px;
                        box-shadow: 0 4px 12px rgba(200, 16, 46, 0.3);
                        margin: 5px;
                    }}
                    .button:hover {{
                        background: #a00d24;
                    }}
                    .button-secondary {{
                        background: #003366;
                    }}
                    .button-secondary:hover {{
                        background: #002244;
                    }}
                    .contact-section {{
                        background: #f1f5f9;
                        padding: 25px;
                        border-radius: 10px;
                        margin-top: 30px;
                    }}
                    .contact-section h3 {{
                        margin: 0 0 15px 0;
                        color: #1f2937;
                        font-size: 18px;
                    }}
                    .contact-info {{
                        color: #475569;
                        font-size: 14px;
                        line-height: 1.8;
                    }}
                    .contact-info strong {{
                        color: #1f2937;
                    }}
                    .footer {{
                        background: #1f2937;
                        color: #94a3b8;
                        padding: 25px 30px;
                        text-align: center;
                        font-size: 13px;
                    }}
                    .footer strong {{
                        color: #ffa500;
                    }}
                    @media only screen and (max-width: 600px) {{
                        .detail-grid {{
                            grid-template-columns: 1fr;
                        }}
                        .schedule-box .time {{
                            font-size: 24px;
                        }}
                    }}
                </style>
            </head>
            <body>
                <div class='email-container'>
                    <div class='header'>
                        <div class='emoji'>🎓</div>
                        <h1>Enrollment Approved!</h1>
                        <p style='margin: 10px 0 0; opacity: 0.95;'>Fort Bonifacio Elementary School</p>
                    </div>
                    
                    <div class='content'>
                        <div class='greeting'>
                            Dear <strong>{enrollment.ParentName}</strong>,
                        </div>
                        
                        <div class='success-message'>
                            <h2>✅ Congratulations!</h2>
                            <p>We are pleased to inform you that the enrollment application for <strong>{enrollment.StudentName}</strong> has been successfully approved!</p>
                        </div>

                        <div class='details-section'>
                            <h3>📋 Enrollment Details</h3>
                            
                            <div class='detail-grid'>
                                <div class='detail-item'>
                                    <strong>Student Name</strong>
                                    <span>{enrollment.StudentName}</span>
                                </div>
                                <div class='detail-item'>
                                    <strong>Grade Level</strong>
                                    <span>Grade {enrollment.GradeLevel}</span>
                                </div>
                                <div class='detail-item'>
                                    <strong>Section</strong>
                                    <span>Section {enrollment.Section}</span>
                                </div>
                                <div class='detail-item'>
                                    <strong>School Year</strong>
                                    <span>{DateTime.Now.Year} - {DateTime.Now.Year + 1}</span>
                                </div>
                                <div class='detail-item'>
                                    <strong>Assigned Room</strong>
                                    <span>{assignedRoom}</span>
                                </div>
                                <div class='detail-item'>
                                    <strong>Class Adviser</strong>
                                    <span>{professorName}</span>
                                </div>
                            </div>

                            <div class='detail-grid'>
                                <div class='detail-item'>
                                    <strong>Parent/Guardian</strong>
                                    <span>{enrollment.ParentName}</span>
                                </div>
                                <div class='detail-item'>
                                    <strong>Contact Number</strong>
                                    <span>{enrollment.ContactNumber}</span>
                                </div>
                            </div>

                            <div class='detail-item' style='margin-top: 15px;'>
                                <strong>Home Address</strong>
                                <span>{enrollment.Address}</span>
                            </div>

                            <div class='detail-item' style='margin-top: 15px;'>
                                <strong>Enrollment Date</strong>
                                <span>{enrollment.EnrollmentDate.ToString("MMMM dd, yyyy")}</span>
                            </div>
                        </div>

                        <div class='schedule-box'>
                            <h3>🕐 Class Schedule</h3>
                            <div class='time'>{classSchedule}</div>
                            <div class='shift'>{shift} Shift</div>
                            <p style='margin: 15px 0 0; opacity: 0.95; font-size: 14px;'>Monday to Friday | 5 Hours Daily</p>
                        </div>

                        <div class='important-info'>
                            <h3>⚠️ Important Reminders</h3>
                            <ul>
                                <li><strong>First Day of Class:</strong> Please check the school calendar for the start date.</li>
                                <li><strong>Required Items:</strong> School uniform, supplies, and ID requirements will be sent separately.</li>
                                <li><strong>Orientation:</strong> Watch for updates about the parent-student orientation schedule.</li>
                                <li><strong>Contact Your Adviser:</strong> Feel free to reach out to {professorName} at {professorEmail} for any questions.</li>
                            </ul>
                        </div>

                        <div class='button-container'>
                            <a href='https://localhost:7065/Student/ViewEnrollment' class='button'>View Full Details</a>
                            <a href='https://localhost:7065/Student/PrintEnrollment' class='button button-secondary'>Print Enrollment</a>
                        </div>

                        <div class='contact-section'>
                            <h3>📞 Need Help?</h3>
                            <div class='contact-info'>
                                <p>If you have any questions or concerns, please don't hesitate to contact us:</p>
                                <p><strong>📍 Address:</strong> Fort Bonifacio, Taguig City</p>
                                <p><strong>📞 Phone:</strong> (02) 239 8307</p>
                                <p><strong>✉️ Email:</strong> fortbonifacio01@gmail.com</p>
                                <p><strong>🕐 Office Hours:</strong> Monday - Friday, 8:00 AM - 4:00 PM</p>
                            </div>
                        </div>

                        <p style='margin-top: 30px; color: #64748b; text-align: center; font-size: 14px;'>
                            Welcome to Fort Bonifacio Elementary School Family! 🎉<br>
                            <em>Together, we build the future — one learner at a time.</em>
                        </p>
                    </div>
                    
                    <div class='footer'>
                        <p style='margin: 0 0 10px;'>&copy; {DateTime.Now.Year} Fort Bonifacio Elementary School. All Rights Reserved.</p>
                        <p style='margin: 0;'>This is an automated message. Please do not reply to this email.</p>
                        <p style='margin: 10px 0 0;'>Developed for <strong>Official Educational Purposes</strong></p>
                    </div>
                </div>
            </body>
            </html>";

                await _emailService.SendEmailAsync(enrollment.User.Email, subject, htmlMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send acceptance email");
            }

            TempData["Success"] = $"Enrollment for {enrollment.StudentName} has been approved and detailed email notification sent.";
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
                // Check if section already has an assigned professor
                if (model.Section.HasValue)
                {
                    var existingProfessor = await _context.Users
                        .FirstOrDefaultAsync(u => u.Role == "professor" &&
                                                 u.AssignedGradeLevel == model.GradeLevel &&
                                                 u.AssignedSection == model.Section);

                    if (existingProfessor != null)
                    {
                        ModelState.AddModelError("Section",
                            $"Grade {model.GradeLevel} - Section {model.Section} already has an assigned professor: {existingProfessor.FullName}");
                        return View(model);
                    }
                }

                // Get room assignment
                string assignedRoom = model.Section.HasValue
                    ? RoomAssignmentHelper.GetRoomForSection(model.GradeLevel, model.Section.Value)
                    : null;

                var user = new Users
                {
                    FullName = model.FullName,
                    UserName = model.Email,
                    Email = model.Email,
                    EmailConfirmed = true,
                    Role = "professor",
                    AssignedGradeLevel = model.GradeLevel,
                    AssignedSection = model.Section,
                    AssignedRoom = assignedRoom // NEW: Assign room
                };

                var result = await _userManager.CreateAsync(user, model.Password);

                if (result.Succeeded)
                {
                    TempData["Success"] = $"Professor {model.FullName} has been added successfully and assigned to {assignedRoom}.";
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

                // Check if the new section assignment conflicts with existing professors
                if (model.Section.HasValue)
                {
                    var existingProfessor = await _context.Users
                        .FirstOrDefaultAsync(u => u.Role == "professor" &&
                                                 u.AssignedGradeLevel == model.GradeLevel &&
                                                 u.AssignedSection == model.Section &&
                                                 u.Id != model.Id);

                    if (existingProfessor != null)
                    {
                        ModelState.AddModelError("Section",
                            $"Grade {model.GradeLevel} - Section {model.Section} already has an assigned professor: {existingProfessor.FullName}");
                        return View(model);
                    }
                }

                // Update room assignment
                string assignedRoom = model.Section.HasValue
                    ? RoomAssignmentHelper.GetRoomForSection(model.GradeLevel, model.Section.Value)
                    : null;

                professor.FullName = model.FullName;
                professor.Email = model.Email;
                professor.UserName = model.Email;
                professor.AssignedGradeLevel = model.GradeLevel;
                professor.AssignedSection = model.Section;
                professor.AssignedRoom = assignedRoom; // NEW: Update room

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
            enrollment.Status = "pending";

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


        [HttpGet]
        public async Task<IActionResult> GetAvailableSections(string gradeLevel)
        {
            var takenSections = await _context.Users
                .Where(u => u.Role == "professor" &&
                           u.AssignedGradeLevel == gradeLevel &&
                           u.AssignedSection.HasValue)
                .Select(u => new
                {
                    section = u.AssignedSection.Value,
                    professorName = u.FullName
                })
                .ToListAsync();

            return Json(new { takenSections });
        }
    }
}