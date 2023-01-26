using Microsoft.AspNetCore.Authorization;
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
            var result = _genreService.GetAllAsync().Result;
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpGet("{id}")]
        public IActionResult GetById(Guid id)
        {
            var result = _genreService.GetByIdAsync(id).Result;
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpGet("code/{code}")]
        public IActionResult GetByGenreCode(int code)
        {
            var result = _genreService.GetByGenreCodeAsync(code).Result;
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpPost]
        public IActionResult Add(Genre genre)
        {
            var result = _genreService.AddAsync(genre).Result;
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpDelete]
        public IActionResult Delete(Genre genre)
        {
            var result = _genreService.DeleteAsync(genre).Result;
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpPut]
        public IActionResult Update(Genre genre)
        {
            var result = _genreService.UpdateAsync(genre).Result;
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }
    }
}
