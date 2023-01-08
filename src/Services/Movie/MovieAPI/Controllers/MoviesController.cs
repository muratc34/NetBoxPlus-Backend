using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MovieAPI.Model;
using MovieAPI.Services;

namespace MovieAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MoviesController : ControllerBase
    {
        IMovieService _movieService;

        public MoviesController(IMovieService movieService)
        {
            _movieService = movieService;
        }

        [HttpGet]
        public IActionResult GetAll()
        {
            var result = _movieService.GetAll().Result;
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpGet("{id}")]
        public IActionResult GetById(Guid id)
        {
            var result = _movieService.GetById(id).Result;
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpPost]
        public IActionResult Add(IFormFile poster, IFormFile backdropPic, [FromForm] MovieDto movieDto)
        {
            var result = _movieService.Add(poster, backdropPic, movieDto).Result;
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpDelete]
        public IActionResult Delete(Movie movie)
        {
            var result = _movieService.Delete(movie).Result;
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpPut]
        public IActionResult Update(Movie movie)
        {
            var result = _movieService.Update(movie).Result;
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }
    }
}
