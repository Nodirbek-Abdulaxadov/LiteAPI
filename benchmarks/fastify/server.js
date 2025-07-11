import Fastify from 'fastify';

const fastify = Fastify({ logger: true });

const summaries = [
    "Freezing", "Bracing", "Chilly", "Cool", "Mild",
    "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
];

fastify.get('/weatherforecast', async (request, reply) => {
    const forecast = Array.from({ length: 50 }, (_, index) => {
        const date = new Date();
        date.setDate(date.getDate() + index + 1);

        const temperatureC = Math.floor(Math.random() * (35 - (-20) + 1)) + (-20);
        const summary = summaries[Math.floor(Math.random() * summaries.length)];
        const temperatureF = 32 + Math.floor(temperatureC / 0.5556);

        return {
            date: date.toISOString().split('T')[0],
            temperatureC,
            temperatureF,
            summary
        };
    });

    return forecast;
});

// Start server
const start = async () => {
    try {
        await fastify.listen({ port: 3000, host: '0.0.0.0' });
        console.log('Fastify server running at http://localhost:3000');
    } catch (err) {
        fastify.log.error(err);
        process.exit(1);
    }
};

start();