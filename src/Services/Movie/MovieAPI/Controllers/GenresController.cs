using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MovieAPI.Model;
using MovieAPI.Services;

namespace MovieAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class GenresController : ControllerBase
    {
        IGenreService _genreService;

        public GenresController(IGenreService genreService)
        {
            _genreService = genreService;
        }

        [HttpGet]
        public IActionResult GetAll()
        {
            var result = _genreService.GetAll().Result;
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpGet("{id}")]
        public IActionResult GetById(Guid id)
        {
            var result = _genreService.GetById(id).Result;
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpGet("code/{code}")]
        public IActionResult GetByGenreCode(int code)
        {
            var result = _genreService.GetByGenreCode(code).Result;
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpPost]
        public IActionResult Add(Genre genre)
        {
            var result = _genreService.Add(genre).Result;
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpDelete]
        public IActionResult Delete(Genre genre)
        {
            var result = _genreService.Delete(genre).Result;
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpPut]
        public IActionResult Update(Genre genre)
        {
            var result = _genreService.Update(genre).Result;
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }
    }
}
