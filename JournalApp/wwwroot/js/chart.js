window.renderMoodChart = (data) => {
    const ctx = document.getElementById("moodChart");

    new Chart(ctx, {
        type: "bar",
        data: {
            labels: data.map(x => x.mood),
            datasets: [{
                data: data.map(x => x.count),
                backgroundColor: "#0ea5e9",
                borderRadius: 12
            }]
        },
        options: {
            responsive: true,
            plugins: { legend: { display: false } },
            scales: {
                y: { beginAtZero: true }
            }
        }
    });
};
