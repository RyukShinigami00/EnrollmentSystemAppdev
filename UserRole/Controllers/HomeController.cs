using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UserRoles.Data;
using UserRoles.Models;
using UserRoles.Helpers;

namespace UserRoles.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly AppDbContext _context;
        private readonly UserManager<Users> _userManager;

        public HomeController(
            ILogger<HomeController> logger,
            AppDbContext context,
            UserManager<Users> userManager)
        {
            _logger = logger;
            _context = context;
            _userManager = userManager;
        }

        public IActionResult Index()
        {
            return View();
        }

        [Authorize]
        public IActionResult Privacy()
        {
            return View();
        }

        [Authorize(Roles = "Admin")]
        public IActionResult Admin()
        {
            return View();
        }

        [Authorize(Roles = "User")]
        public async Task<IActionResult> User()
        {
            var userId = HttpContext.Session.GetString("UserId");
            var user = await _userManager.FindByIdAsync(userId);

            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

           
            var enrollment = await _context.Enrollments
                .Include(e => e.User)
                .FirstOrDefaultAsync(e => e.UserId == userId);

            
            Users assignedProfessor = null;
            string assignedRoom = null;
            string classSchedule = null;
            string shift = null;

            if (enrollment != null && enrollment.Status == "approved" && enrollment.Section.HasValue)
            {
                assignedProfessor = await _context.Users
                    .FirstOrDefaultAsync(u => u.Role == "professor" &&
                                             u.AssignedGradeLevel == enrollment.GradeLevel &&
                                             u.AssignedSection == enrollment.Section);

           
                if (assignedProfessor != null && !string.IsNullOrEmpty(assignedProfessor.AssignedRoom))
                {
                    assignedRoom = assignedProfessor.AssignedRoom;
                }
                else
                {
                    assignedRoom = RoomAssignmentHelper.GetRoomForSection(enrollment.GradeLevel, enrollment.Section.Value);
                }

                var grade = int.Parse(enrollment.GradeLevel);
                bool isMorningShift = grade % 2 == 0; 

                shift = isMorningShift ? "Morning" : "Afternoon";
                classSchedule = isMorningShift ? "7:00 AM - 12:00 PM" : "1:00 PM - 6:00 PM";
            }

            ViewBag.Enrollment = enrollment;
            ViewBag.AssignedProfessor = assignedProfessor;
            ViewBag.AssignedRoom = assignedRoom;
            ViewBag.ClassSchedule = classSchedule;
            ViewBag.Shift = shift;

            return View();
        }

        public IActionResult AboutUs()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}