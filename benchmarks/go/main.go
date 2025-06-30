package main

import (
	"bufio"
	"encoding/json"
	"fmt"
	"math/rand"
	"net"
	"strings"
	"time"
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
	listener, err := net.Listen("tcp", "127.0.0.1:7055")
	if err != nil {
		panic(err)
	}
	defer listener.Close()

	fmt.Println("LiteAPI-Go-TcpServer: http://localhost:7055")

	for {
		conn, err := listener.Accept()
		if err != nil {
			fmt.Println("Accept error:", err)
			continue
		}
		go handleConnection(conn)
	}
}

func handleConnection(conn net.Conn) {
	defer conn.Close()

	conn.SetReadDeadline(time.Now().Add(5 * time.Second))
	reader := bufio.NewReader(conn)

	line, err := reader.ReadString('\n')
	if err != nil {
		fmt.Println("Read error:", err)
		return
	}

	if !strings.HasPrefix(line, "GET ") {
		return
	}

	var response []byte
	status := "200 OK"

	if strings.Contains(line, "/weatherforecast") {
		forecasts := make([]WeatherForecast, 500)
		now := time.Now()
		for i := 0; i < 500; i++ {
			tempC := rand.Intn(75) - 20
			forecasts[i] = WeatherForecast{
				Date:         now.AddDate(0, 0, i+1).Format("2006-01-02"),
				TemperatureC: tempC,
				TemperatureF: 32 + int(float64(tempC)/0.5556),
				Summary:      summaries[rand.Intn(len(summaries))],
			}
		}
		response, _ = json.Marshal(forecasts)
	} else {
		status = "404 Not Found"
		response = []byte(`{"error":"Not found"}`)
	}

	header := fmt.Sprintf("HTTP/1.1 %s\r\nContent-Type: application/json\r\nContent-Length: %d\r\nConnection: close\r\n\r\n",
		status, len(response))

	conn.Write([]byte(header))
	conn.Write(response)
}
