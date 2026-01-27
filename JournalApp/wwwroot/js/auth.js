
window.authHelper = {

    login: function () {
        sessionStorage.setItem("isLoggedIn", "true");
        return true;
    },

    isLoggedIn: function () {
        return sessionStorage.getItem("isLoggedIn") === "true";
    },

    logout: function () {
        sessionStorage.removeItem("isLoggedIn");
        return true;
    }
};

window.showToast = function (message) {
    alert(message);
};
