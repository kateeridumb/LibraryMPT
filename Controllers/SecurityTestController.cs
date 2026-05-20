using Microsoft.AspNetCore.Mvc;

namespace LibraryMPT.Controllers
{
    public class SecurityTestController : Controller
    {
        private readonly IWebHostEnvironment _environment;

        public SecurityTestController(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        [HttpGet]
        public IActionResult Index()
        {
            if (!_environment.IsDevelopment())
            {
                return NotFound();
            }

            return View();
        }
    }
}

