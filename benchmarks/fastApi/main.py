# main.py
from fastapi import FastAPI
from pydantic import BaseModel
from datetime import date, timedelta
import random

app = FastAPI()

summaries = [
    "Freezing", "Bracing", "Chilly", "Cool", "Mild",
    "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
]

class WeatherForecast(BaseModel):
    date: date
    temperatureC: int
    summary: str

    @property
    def temperatureF(self):
        return 32 + int(self.temperatureC / 0.5556)

@app.get("/weatherforecast")
def get_weather_forecast():
    forecast = [
        WeatherForecast(
            date=date.today() + timedelta(days=i),
            temperatureC=random.randint(-20, 35),
            summary=random.choice(summaries)
        ) for i in range(1, 51)
    ]
    return forecast
