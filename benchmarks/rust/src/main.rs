use actix_web::{get, App, HttpResponse, HttpServer, Responder};
use rand::Rng;
use serde::Serialize;
use chrono::{Duration, Local, NaiveDate};

#[derive(Serialize)]
struct WeatherForecast {
    date: String,
    #[serde(rename = "temperatureC")]
    temperature_c: i32,
    #[serde(rename = "temperatureF")]
    temperature_f: i32,
    summary: String,
}

#[get("/weatherforecast")]
async fn weatherforecast() -> impl Responder {
    let forecasts = generate_forecast();
    HttpResponse::Ok().json(forecasts)
}

fn generate_forecast() -> Vec<WeatherForecast> {
    let summaries = [
        "Freezing", "Bracing", "Chilly", "Cool", "Mild",
        "Warm", "Balmy", "Hot", "Sweltering", "Scorching",
    ];

    let mut rng = rand::thread_rng();
    let now: NaiveDate = Local::now().date_naive();

    (1..=50)
        .map(|i| {
            let temp_c = rng.gen_range(-20..55);
            WeatherForecast {
                date: (now + Duration::days(i as i64)).to_string(),
                temperature_c: temp_c,
                temperature_f: 32 + (temp_c as f64 / 0.5556) as i32,
                summary: summaries[rng.gen_range(0..summaries.len())].to_string(),
            }
        })
        .collect()
}

#[actix_web::main]
async fn main() -> std::io::Result<()> {
    println!("Actix-Web server running at http://127.0.0.1:9055");

    HttpServer::new(|| {
        App::new()
            .service(weatherforecast)
    })
    .bind(("127.0.0.1", 9055))?
    .run()
    .await
}
