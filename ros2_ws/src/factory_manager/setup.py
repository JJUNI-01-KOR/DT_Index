from glob import glob
from setuptools import setup

package_name = 'factory_manager'

setup(
    name=package_name,
    version='0.0.1',
    packages=[package_name],
    data_files=[
        ('share/ament_index/resource_index/packages', ['resource/' + package_name]),
        ('share/' + package_name, ['package.xml']),
        ('share/' + package_name + '/config', glob('config/*.yaml')),
    ],
    install_requires=['setuptools'],
    zip_safe=True,
    maintainer='INDEX',
    maintainer_email='index-team@example.com',
    description='공정 FSM과 라인 명령권 (factory_manager 노드)',
    license='Apache-2.0',
    tests_require=['pytest'],
    entry_points={
        'console_scripts': [
            'factory_manager_node = factory_manager.factory_manager_node:main',
        ],
    },
)
