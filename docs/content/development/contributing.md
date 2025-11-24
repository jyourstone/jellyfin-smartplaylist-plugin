# Contributing

Thank you for your interest in contributing to the SmartPlaylist plugin! This guide will help you get started.

## Prerequisites

Before contributing, make sure you:

- Have set up the [local development environment](building-locally.md)
- Are familiar with Git and GitHub
- Have tested your changes locally

## How to Contribute

### 1. Fork the Repository

1. Go to the [repository page](https://github.com/jyourstone/jellyfin-smartlists-plugin)
2. Click the **Fork** button in the top right
3. This creates a copy of the repository in your GitHub account

### 2. Clone Your Fork

```bash
git clone https://github.com/YOUR_USERNAME/jellyfin-smartplaylist-plugin.git
cd jellyfin-smartplaylist-plugin
```

### 3. Make Your Changes

1. Create a new branch for your changes:
   ```bash
   git checkout -b your-feature-name
   ```

2. Make your changes to the codebase
3. Test your changes using the local development environment (see [Building Locally](building-locally.md))
4. Commit your changes:
   ```bash
   git add .
   git commit -m "Description of your changes"
   ```

### 4. Push and Create a Pull Request

1. Push your branch to your fork:
   ```bash
   git push origin your-feature-name
   ```

2. Go to your fork on GitHub
3. Click **Contribute** → **Open Pull Request**
4. Select your branch and create a pull request to the `main` branch of the original repository
5. Fill out the pull request description explaining your changes

Your pull request will be reviewed, and once approved, it will be merged into the main branch.

## What to Contribute

All contributions are welcome:

- **Bug fixes** - Report and fix issues you encounter
- **New features** - Add functionality that would benefit users
- **Documentation** - Improve or expand the documentation
- **Code improvements** - Refactor, optimize, or improve existing code
- **Testing** - Add tests or improve test coverage

## Code Guidelines

- Follow existing code style and patterns
- Write clear, descriptive commit messages
- Test your changes thoroughly before submitting
- Update documentation if you add new features or change behavior