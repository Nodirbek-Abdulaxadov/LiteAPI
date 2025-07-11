package main

import (
	"math/rand"
	"time"

	"github.com/gofiber/fiber/v2"
)

var summaries = []string{
	"Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm",
	"Balmy", "Hot", "Sweltering", "Scorching",
}

type WeatherForecast struct {
	Date         string `json:"date"`
	TemperatureC int    `json:"temperatureC"`
	TemperatureF int    `json:"temperatureF"`
	Summary      string `json:"summary"`
}

func main() {
	app := fiber.New(fiber.Config{
		Prefork:               false,
		DisableStartupMessage: true,
	})

	app.Get("/weatherforecast", func(c *fiber.Ctx) error {
		const forecastCount = 50
		forecasts := make([]WeatherForecast, forecastCount)
		now := time.Now()

		for i := 0; i < forecastCount; i++ {
			tempC := rand.Intn(75) - 20 // [-20, 54]
			forecasts[i] = WeatherForecast{
				Date:         now.AddDate(0, 0, i+1).Format("2006-01-02"),
				TemperatureC: tempC,
				TemperatureF: 32 + int(float64(tempC)/0.5556),
				Summary:      summaries[rand.Intn(len(summaries))],
			}
		}

		return c.JSON(forecasts)
	})

	app.Use(func(c *fiber.Ctx) error {
		return c.Status(404).JSON(fiber.Map{
			"error": "Not found",
		})
	})

	println("Fiber server running on http://localhost:7055")
	if err := app.Listen("127.0.0.1:7055"); err != nil {
		panic(err)
	}
}
